using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;

public class AuctionEndDuringPartitionTests
{
    [Test]
    public async Task AuctionEndsDuringPartition_LateBidsAreIgnored_AndWinnerIsCorrect()
    {
        var opt = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite("Filename=:memory:").Options;

        using var db = new AuctionDbContext(opt);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var auctionRepo = new AuctionRepository(db);
        var bidRepo = new BidRepository(db);
        var seq = new FakeSequenceService();
        var regional = new RegionCoordinator("US-East", "EU-West");
        var resolver = new ConflictResolver();

        var svc = new AuctionService(auctionRepo, bidRepo, seq, new BidOrderingService(seq, db), regional, resolver, db);

        var auction = new Auction
        {
            Region = "US-East",
            VehicleId = Guid.NewGuid(),
            State = AuctionState.Running,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            EndTime = DateTime.UtcNow.AddSeconds(5) // termina enquanto particionado
        };
        await svc.CreateAuctionAsync(auction);

        regional.SimulatePartition("US-East", "EU-West");

        var euPending = await svc.PlaceBidAsync(auction.Id, new BidRequest
        {
            OriginRegion = "EU-West",
            TargetRegion = "US-East",
            UserId = "EU",
            Amount = 100
        });
        euPending.Status.Should().Be(BidStatus.PendingPartition);

        var usAccepted = await svc.PlaceBidAsync(auction.Id, new BidRequest
        {
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            UserId = "US",
            Amount = 120
        });
        usAccepted.Status.Should().Be(BidStatus.Accepted);

        await Task.Delay(6000);

        regional.HealPartition("US-East", "EU-West");
        var rec = await svc.ReconcileAuctionAsync(auction.Id);
        rec.Success.Should().BeTrue();

        var loaded = await svc.GetAuctionAsync(auction.Id, ConsistencyLevel.Strong);
        loaded!.HighestAmount.Should().Be(120);
    }
}