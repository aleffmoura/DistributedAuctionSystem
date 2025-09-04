using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Repositories;
using DistributedAuction.Tests.Commons;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;

public class ConflictResolutionTests
{
    private static async Task<AuctionDbContext> NewDbAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(conn)
            .EnableSensitiveDataLogging()
            .Options;
        var db = new AuctionDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    [Test]
    public async Task SameAmount_DuringPartition_Reconcile_PicksDeterministicWinner()
    {
        await using var db = await NewDbAsync();

        var auctionRepo = new AuctionRepository(db);
        var bidRepo = new BidRepository(db);
        var sequence = new FakeSequenceService();
        var ordering = new BidOrderingService(sequence, db);
        var coordinator = new RegionCoordinator("US-East", "EU-West");
        var resolver = new ConflictResolver();
        var svc = new AuctionService(auctionRepo, bidRepo, sequence, ordering, coordinator, resolver, db);

        var auctionReq = new CreateAuctionRequest
        {
            VehicleId = Guid.NewGuid(),
            Region = "US-East",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(1),
            State = AuctionState.Running
        };
        var auction = await svc.CreateAuctionAsync(auctionReq);

        // 1) US dá lance 150 (aceito localmente, seq menor)
        var us = await svc.PlaceBidAsync(auction.Id, new BidRequest
        {
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            UserId = "US-1",
            Amount = 150
        });
        us.Status.Should().Be(BidStatus.Accepted);

        // 2) Partição EU↔US
        coordinator.SimulatePartition("US-East", "EU-West");

        // 3) EU tenta mesmo valor 150 (pendente na outbox)
        var eu = await svc.PlaceBidAsync(auction.Id, new BidRequest
        {
            OriginRegion = "EU-West",
            TargetRegion = "US-East",
            UserId = "EU-1",
            Amount = 150
        });
        eu.Status.Should().Be(BidStatus.PendingPartition);

        // 4) Cura partição e reconcilia
        coordinator.HealPartition("US-East", "EU-West");
        var rec = await svc.ReconcileAuctionAsync(auction.Id);
        rec.Success.Should().BeTrue();

        // 5) Determinístico: vence quem tem sequence menor (o US, que chegou primeiro)
        var loaded = await svc.GetAuctionAsync(auction.Id, ConsistencyLevel.Strong);
        loaded!.HighestAmount.Should().Be(150);

        // Confere se o HighestBidId pertence ao usuário US e são 2 lances no histórico
        var highestBid = await db.Bids.FirstAsync(b => b.Id == loaded.HighestBidId);
        highestBid.UserId.Should().Be("US-1");
        (await db.Bids.CountAsync(b => b.AuctionId == auction.Id)).Should().Be(2);
    }
}