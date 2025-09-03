using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;

public class PartitionSimulationTests
{
    [Test]
    public async Task PartitionScenario_NoBidLost_WinnerCorrect()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(conn)
            .Options;

        await using var db = new AuctionDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var auctionRepo = new AuctionRepository(db);
        var bidRepo = new BidRepository(db);
        var sequence = new FakeSequenceService();
        var ordering = new BidOrderingService(sequence, db);
        var coordinator = new RegionCoordinator("US-East", "EU-West");
        var resolver = new ConflictResolver();
        var svc = new AuctionService(auctionRepo, bidRepo, sequence, ordering, coordinator, resolver, db);

        var auction = new Auction
        {
            VehicleId = Guid.NewGuid(),
            Region = "US-East",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(1),
            State = AuctionState.Running
        };
        await svc.CreateAuctionAsync(auction);

        // Simulate partition
        coordinator.SimulatePartition("US-East", "EU-West");

        // EU user bids on US auction during partition -> pending
        var euResult = await svc.PlaceBidAsync(auction.Id, new BidRequest
        {
            OriginRegion = "EU-West",
            TargetRegion = "US-East",
            UserId = "EU-User",
            Amount = 120
        });
        euResult.Status.Should().Be(BidStatus.PendingPartition);

        // US user bids locally -> accepted
        var usResult = await svc.PlaceBidAsync(auction.Id, new BidRequest
        {
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            UserId = "US-User",
            Amount = 130
        });
        usResult.Status.Should().Be(BidStatus.Accepted);
        (await db.Bids.CountAsync(b => b.AuctionId == auction.Id)).Should().Be(1);

        // Heal and reconcile -> deliver pending EU bid, but US still wins
        coordinator.HealPartition("US-East", "EU-West");
        var rec = await svc.ReconcileAuctionAsync(auction.Id);
        rec.Success.Should().BeTrue();
        var loaded = await svc.GetAuctionAsync(auction.Id, ConsistencyLevel.Strong);
        loaded!.HighestAmount.Should().Be(130);
        (await db.Bids.CountAsync(b => b.AuctionId == auction.Id)).Should().Be(2);
    }
}
