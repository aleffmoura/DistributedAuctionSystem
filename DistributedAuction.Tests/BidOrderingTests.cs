using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;
public class BidOrderingTests
{
    [Test]
    public async Task SequencesAreMonotonicUnderConcurrency()
    {
        await using var db = await NewDbAsync();
        var seqService = new FakeSequenceService();
        var auctionId = Guid.NewGuid();

        var tasks = Enumerable.Range(0, 50).Select(async _ => await seqService.GetNextAsync(auctionId)).ToArray();
        await Task.WhenAll(tasks);
        var results = tasks.Select(t => t.Result).OrderBy(x => x).ToList();
        results.Should().BeInAscendingOrder();
        results.Count.Should().Be(50);
    }
    [Test]
    public async Task GetOrderedBidsAsync_Returns_StrictlyOrderedBySequence()
    {
        await using var db = await NewDbAsync();
        var seqSvc = new FakeSequenceService();
        var ordering = new BidOrderingService(seqSvc, db);

        var auction = new Auction
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            Region = "US-East",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(5),
            State = AuctionState.Running,
            HighestAmount = 0,
            RowVersion = BitConverter.GetBytes(DateTime.UtcNow.Ticks)
        };
        db.Auctions.Add(auction);
        await db.SaveChangesAsync();

        // Inserimos propositalmente fora de ordem (seq 3, 1, 2)
        db.Bids.AddRange(
            new Bid { Id = Guid.NewGuid(), AuctionId = auction.Id, UserId = "u", Amount = 10, Sequence = 3, CreatedAt = DateTime.UtcNow.AddSeconds(3), OriginRegion = "US-East" },
            new Bid { Id = Guid.NewGuid(), AuctionId = auction.Id, UserId = "u", Amount = 10, Sequence = 1, CreatedAt = DateTime.UtcNow.AddSeconds(1), OriginRegion = "US-East" },
            new Bid { Id = Guid.NewGuid(), AuctionId = auction.Id, UserId = "u", Amount = 10, Sequence = 2, CreatedAt = DateTime.UtcNow.AddSeconds(2), OriginRegion = "US-East" }
        );
        await db.SaveChangesAsync();

        var ordered = (await ordering.GetOrderedBidsAsync(auction.Id)).ToList();
        ordered.Select(b => b.Sequence).Should().Equal(1, 2, 3);
    }

    [Test]
    public async Task ValidateBidOrderAsync_Rejects_NonAscendingAmount_And_NonMonotonicSequence()
    {
        await using var db = await NewDbAsync();
        var seqSvc = new FakeSequenceService();
        var ordering = new BidOrderingService(seqSvc, db);

        var auction = new Auction
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            Region = "US-East",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(5),
            State = AuctionState.Running,
            HighestAmount = 100,
            RowVersion = BitConverter.GetBytes(DateTime.UtcNow.Ticks)
        };
        db.Auctions.Add(auction);
        await db.SaveChangesAsync();

        // Simula já existir um bid com seq 5
        db.Bids.Add(new Bid { Id = Guid.NewGuid(), AuctionId = auction.Id, UserId = "a", Amount = 120, Sequence = 5, CreatedAt = DateTime.UtcNow, OriginRegion = "US-East" });
        await db.SaveChangesAsync();

        // 1) Valor menor/igual ao topo => rejeita
        var lowBid = new Bid { AuctionId = auction.Id, UserId = "x", Amount = 100, Sequence = 6 };
        var lowAcc = await ordering.ValidateBidOrderAsync(auction.Id, lowBid);
        lowAcc.Accepted.Should().BeFalse();
        lowAcc.Reason.Should().Contain("greater");

        // 2) Sequência não-monótona => rejeita
        var badSeqBid = new Bid { AuctionId = auction.Id, UserId = "y", Amount = 130, Sequence = 4 };
        var badSeqAcc = await ordering.ValidateBidOrderAsync(auction.Id, badSeqBid);
        badSeqAcc.Accepted.Should().BeFalse();
        badSeqAcc.Reason.Should().Contain("Sequence");

        // 3) Correto => aceita
        var okBid = new Bid { AuctionId = auction.Id, UserId = "z", Amount = 130, Sequence = 6 };
        var okAcc = await ordering.ValidateBidOrderAsync(auction.Id, okBid);
        okAcc.Accepted.Should().BeTrue();
    }

    private static async Task<AuctionDbContext> NewDbAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(conn)
            .Options;

        var db = new AuctionDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}