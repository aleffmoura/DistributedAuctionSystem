using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;

public class CrossRegionBiddingTests
{
    [Test]
    public async Task CrossRegionBid_NoPartition_AcceptsAndSyncs()
    {
        // SQLite in-memory p/ suportar transações
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(conn)
            .EnableSensitiveDataLogging()
            .Options;

        await using var db = new AuctionDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var auctionRepo = new AuctionRepository(db);
        var bidRepo = new BidRepository(db);
        var sequence = new FakeSequenceService();
        var ordering = new BidOrderingService(sequence, db);
        var coordinator = new RegionCoordinator("US-East", "EU-West"); // por padrão, link OK
        var resolver = new ConflictResolver();
        var svc = new AuctionService(auctionRepo, bidRepo, sequence, ordering, coordinator, resolver, db);

        // SUT extra: sync service
        IDatabaseSyncService sync = new DatabaseSyncService(db, svc);

        var auction = new Auction
        {
            VehicleId = Guid.NewGuid(),
            Region = "US-East", // dono do leilão
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(1),
            State = AuctionState.Running
        };
        await svc.CreateAuctionAsync(auction);

        // EU dá lance num leilão US (sem partição)
        var result = await svc.PlaceBidAsync(auction.Id, new BidRequest
        {
            OriginRegion = "EU-West",
            TargetRegion = "US-East",
            UserId = "EU-User",
            Amount = 200
        });

        result.Status.Should().Be(BidStatus.Accepted);
        (await db.Bids.CountAsync(b => b.AuctionId == auction.Id)).Should().Be(1);

        // Sincroniza outbox destinada à EU-West (BidAccepted destinado ao "peer")
        var processed = await sync.PushOutboxAsync("EU-West");
        processed.Should().BeGreaterThan(0);

        // Estado do leilão no dono deve refletir o lance
        var loaded = await svc.GetAuctionAsync(auction.Id, ConsistencyLevel.Strong);
        loaded!.HighestAmount.Should().Be(200);
        loaded.State.Should().Be(AuctionState.Running);
    }
}