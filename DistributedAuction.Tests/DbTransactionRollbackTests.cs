using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Repositories;
using DistributedAuction.Infrastructure.Services;
using DistributedAuction.Tests.Commons;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;

public class DbTransactionRollbackTests
{
    private sealed class ThrowingDbContext(DbContextOptions<AuctionDbContext> options) : AuctionDbContext(options)
    {
        public bool ThrowOnAuditNow { get; set; } = false;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnAuditNow && ChangeTracker.Entries<AuditEntry>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Simulated failure while persisting AuditEntry");
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    [Test]
    public async Task PlaceBid_WhenAuditSaveFails_ShouldRollback_AllBidAndAuctionChanges()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(conn)
            .Options;

        // schema
        await using (var init = new AuctionDbContext(options))
            await init.Database.MigrateAsync();

        // usa o DbContext com toggle
        await using var db = new ThrowingDbContext(options);

        var auctionRepo = new AuctionRepository(db);
        var bidRepo = new BidRepository(db);
        var seqSvc = new AuctionSequenceService(db);
        var ordering = new BidOrderingService(seqSvc, db);
        var region = new SingleRegionCoordinator();
        var resolver = new NoopResolver();

        var svc = new AuctionService(auctionRepo, bidRepo, seqSvc, ordering, region, resolver, db);

        // 1) criação do leilão: NÃO lançar aqui
        db.ThrowOnAuditNow = false;
        var auction = await svc.CreateAuctionAsync(new CreateAuctionRequest
        {
            VehicleId = Guid.NewGuid(),
            Region = "US",
            StartTime = DateTime.UtcNow.AddSeconds(-2),
            EndTime = DateTime.UtcNow.AddMinutes(2),
            State = AuctionState.Created
        });

        // 2) agora sim: queremos falhar ao salvar o Audit do lance
        db.ThrowOnAuditNow = true;

        var act = async () => await svc.PlaceBidAsync(auction.Id, new BidRequest
        {
            Amount = 100m,
            UserId = "alice",
            OriginRegion = "US",
            TargetRegion = "US"
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*AuditEntry*");

        // garante que nada foi persistido
        await using (var verifyDb = new AuctionDbContext(options))
        {
            var verifyAuctionRepo = new AuctionRepository(verifyDb);
            var verifyBidRepo = new BidRepository(verifyDb);

            var fresh = await verifyAuctionRepo.GetAsync(auction.Id);
            fresh!.HighestAmount.Should().Be(0m);
            fresh.HighestBidId.Should().BeNull();

            (await verifyBidRepo.CountForAuction(auction.Id)).Should().Be(0);
        }
    }
}