using DistributedAuction.Domain.DomainServices.Implementations.Auctions;
using DistributedAuction.Domain.DomainServices.Implementations.Bids;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Tests.Commons;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DistributedAuction.Tests;

public class OptimisticConcurrencyTests
{
    private readonly AuctionDomainService _domainService = new();
    private readonly BidDomainService _bidDomainService;
    private readonly FakeSequenceService seqService;

    public OptimisticConcurrencyTests()
    {
        seqService = new FakeSequenceService();
        _bidDomainService = new(seqService);
    }
    private static DbContextOptions<AuctionDbContext> BuildOptions(SqliteConnection conn) =>
        new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(conn)
            .EnableSensitiveDataLogging()
            .Options;

    [Test]
    public async Task Updating_Auction_From_Two_Contexts_Should_Throw_DbUpdateConcurrencyException()
    {
        // 1) Uma única conexão SQLite in-memory compartilhada entre 2 DbContexts
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var options = BuildOptions(conn);

        // 2) Cria schema e semeia um leilão com RowVersion inicial
        await using (var seed = new AuctionDbContext(options))
        {
            await seed.Database.MigrateAsync();

            var auction = _domainService.Create(new Domain.Models.CreateAuctionRequest
            {
                VehicleId = Guid.NewGuid(),
                Region = "US-East",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(10),
                State = AuctionState.Running
            });

            seed.Auctions.Add(auction);
            await seed.SaveChangesAsync();
        }

        await using var db1 = new AuctionDbContext(options);
        await using var db2 = new AuctionDbContext(options);

        var a1 = await db1.Auctions.SingleAsync();
        var a2 = await db2.Auctions.SingleAsync();

        var bid1 = _bidDomainService.Create(a1.Id, new Domain.Models.BidRequest
        {
            UserId = "u",
            Amount = 100m,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "1"
        }, false, 3);

        a1 = await
            _domainService.UpdateIfWinner(a1, bid1, new Mock<IAuctionRepository>().Object);

        await db1.SaveChangesAsync();

        var bid2 = _bidDomainService.Create(a1.Id, new Domain.Models.BidRequest
        {
            UserId = "u",
            Amount = 120m,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "1"
        }, false, 3);

        a2 = await
            _domainService.UpdateIfWinner(a2, bid2, new Mock<IAuctionRepository>().Object);

        Func<Task> act = async () => await db2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await db2.Entry(a2).ReloadAsync();
        a2.HighestAmount.Should().Be(100m);
    }
}