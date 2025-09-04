using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Repositories;
using DistributedAuction.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;

public class LoadSimulationTests
{
    [Test]
    public async Task ConcurrentBids_ShouldKeepMonotonicSequence_AndPickHighestWinner()
    {
        // 1) Usar DB em memória compartilhado entre conexões
        var connectionString = "Data Source=file:auction-load;Mode=Memory;Cache=Shared";

        // 2) Abrir conexão âncora p/ manter o DB vivo
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();

        // 3) Criar schema com um contexto temporário
        var initOptions = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(keeper) // usa a conexão já aberta
            .Options;
        await using (var init = new AuctionDbContext(initOptions))
            await init.Database.EnsureCreatedAsync();

        // 4) Função fábrica para criar contexto/serviço NOVOS (e conexão nova) por task
        AuctionService CreateService(out AuctionDbContext ctx)
        {
            var conn = new SqliteConnection(connectionString);
            conn.Open(); // cada serviço tem sua conexão
            var options = new DbContextOptionsBuilder<AuctionDbContext>()
                .UseSqlite(conn)
                .Options;

            ctx = new AuctionDbContext(options);

            var auctionRepo = new AuctionRepository(ctx);
            var bidRepo = new BidRepository(ctx);
            var seqSvc = new AuctionSequenceService(ctx);
            var ordering = new BidOrderingService(seqSvc, ctx);
            var region = new HealthyDualRegionCoordinator("US", "EU");
            var resolver = new NoopResolver();

            return new AuctionService(auctionRepo, bidRepo, seqSvc, ordering, region, resolver, ctx);
        }

        // 5) Criar o leilão uma vez (com um contexto dedicado)
        Guid auctionId;
        {
            var svc = CreateService(out var ctx);
            var created = await svc.CreateAuctionAsync(new CreateAuctionRequest
            {
                VehicleId = Guid.NewGuid(),
                Region = "US",
                StartTime = DateTime.UtcNow.AddSeconds(-2),
                EndTime = DateTime.UtcNow.AddMinutes(2),
                State = AuctionState.Created
            });
            auctionId = created.Id;
            await ctx.Database.CloseConnectionAsync(); // fecha conexão desse contexto
            await ctx.DisposeAsync();
        }

        // 6) Disparar lances concorrentes usando serviços/contextos independentes
        var total = 200;
        var tasks = new List<Task>();
        for (int i = 1; i <= total; i++)
        {
            var amount = i; // crescente
            var origin = (i % 2 == 0) ? "US" : "EU";
            var user = $"u{i}";

            tasks.Add(Task.Run(async () =>
            {
                var svc = CreateService(out var ctx);
                try
                {
                    await svc.PlaceBidAsync(auctionId, new BidRequest
                    {
                        Amount = amount,
                        UserId = user,
                        OriginRegion = origin,
                        TargetRegion = "US"
                    });
                }
                finally
                {
                    await ctx.Database.CloseConnectionAsync();
                    await ctx.DisposeAsync();
                }
            }));
        }

        await Task.WhenAll(tasks);

        // 7) Verificação com um novo contexto limpo
        await using (var verifyConn = new SqliteConnection(connectionString))
        {
            await verifyConn.OpenAsync();
            var options = new DbContextOptionsBuilder<AuctionDbContext>()
                .UseSqlite(verifyConn)
                .Options;

            await using var verifyDb = new AuctionDbContext(options);
            var auctionRepo = new AuctionRepository(verifyDb);
            var bidRepo = new BidRepository(verifyDb);

            var fresh = await auctionRepo.GetAsync(auctionId);

            var history = await bidRepo.GetHistoryAsync(auctionId, take: total + 100);

            // Invariantes corretos para concorrência com "English auction":
            history.Count.Should().BeGreaterThan(0);
            history.Count.Should().BeLessOrEqualTo(total);

            // Sequence única e crescente
            history.Select(b => b.Sequence)
                   .Should().OnlyHaveUniqueItems()
                   .And.BeInAscendingOrder();

            // Amounts crescentes entre os bids aceitos
            history.Select(b => b.Amount)
                   .Should().BeInAscendingOrder(); // Garante ordem crescente (não estritamente crescente)

            // Winner e HighestAmount coerentes
            fresh!.HighestAmount.Should().Be(history.Max(b => b.Amount));
            fresh.HighestBidId.Should().Be(history.OrderBy(b => b.Sequence).Last().Id);

            // limite superior esperado pelo input
            history.Max(b => b.Amount).Should().BeLessOrEqualTo(total);
        }
    }
}