using DistributedAuction.Application.Services;
using DistributedAuction.Domain.DomainServices.Implementations.Auctions;
using DistributedAuction.Domain.DomainServices.Implementations.Bids;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Services;
using DistributedAuction.Tests.Commons;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DistributedAuction.Tests;
public class BidOrderingTests
{
    private AuctionDomainService _domainService = new();
    private BidDomainService _bidDomainService;
    private AuctionSequenceService seqService;

    [Test]
    public async Task SequencesAreMonotonicUnderConcurrency()
    {
        await using var db = await NewDbAsync();
        seqService = new AuctionSequenceService(db);
        _bidDomainService = new(seqService);
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
        var seqSvc = new AuctionSequenceService(db);
        _bidDomainService = new(seqSvc);
        var ordering = new BidOrderingService(seqSvc, db);

        var auction = _domainService.Create(new Domain.Models.CreateAuctionRequest
        {
            VehicleId = Guid.NewGuid(),
            Region = "US-East",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(5),
            State = AuctionState.Running
        });

        db.Auctions.Add(auction);
        await db.SaveChangesAsync();

        var bid1 = _bidDomainService.Create(auction.Id, new Domain.Models.BidRequest
        {
            UserId = "u",
            Amount = 10,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "3"
        }, false, 3);
        var bid2 = _bidDomainService.Create(auction.Id, new Domain.Models.BidRequest
        {
            UserId = "u",
            Amount = 10,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "1"
        }, false, 1);
        var bid3 = _bidDomainService.Create(auction.Id, new Domain.Models.BidRequest
        {
            UserId = "u",
            Amount = 10,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "2"
        }, false, 2);

        db.Bids.AddRange(bid1, bid2, bid3);
        await db.SaveChangesAsync();

        var ordered = (await ordering.GetOrderedBidsAsync(auction.Id)).ToList();
        ordered.Select(b => b.Sequence).Should().Equal(1, 2, 3);
    }

    [Test]
    public async Task ValidateBidOrderAsync_Rejects_NonAscendingAmount_And_NonMonotonicSequence()
    {
        await using var db = await NewDbAsync();
        var seqSvc = new AuctionSequenceService(db);
        var ordering = new BidOrderingService(seqSvc, db);

        var auction = _domainService.Create(new Domain.Models.CreateAuctionRequest
        {
            VehicleId = Guid.NewGuid(),
            Region = "US-East",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(5),
            State = AuctionState.Running
        });
        var bid = _bidDomainService.Create(auction.Id, new Domain.Models.BidRequest
        {
            UserId = "u",
            Amount = 100,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "1"
        }, false, 3);

        auction = await
            _domainService.UpdateIfWinner(auction, bid, new Mock<IAuctionRepository>().Object);

        db.Auctions.Add(auction);
        await db.SaveChangesAsync();

        var bid2 = _bidDomainService.Create(auction.Id, new Domain.Models.BidRequest
        {
            UserId = "a",
            Amount = 120,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "1"
        }, false, 5);
        db.Bids.Add(bid2);
        await db.SaveChangesAsync();

        // 1) Valor menor/igual ao topo => rejeita
        var lowBid = _bidDomainService.Create(auction.Id, new Domain.Models.BidRequest
        {
            UserId = "x",
            Amount = 100,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "1"
        }, false, 6);
        var lowAcc = await ordering.ValidateBidOrderAsync(auction.Id, lowBid);
        lowAcc.Accepted.Should().BeFalse();
        lowAcc.Reason.Should().Contain("greater");

        // 2) Sequência não-monótona => rejeita
        var badSeqBid = _bidDomainService.Create(auction.Id, new Domain.Models.BidRequest
        {
            UserId = "x",
            Amount = 130,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "1"
        }, false, 4);
        var badSeqAcc = await ordering.ValidateBidOrderAsync(auction.Id, badSeqBid);
        badSeqAcc.Accepted.Should().BeFalse();
        badSeqAcc.Reason.Should().Contain("Sequence");

        // 3) Correto => aceita
        var okBid = _bidDomainService.Create(auction.Id, new Domain.Models.BidRequest
        {
            UserId = "z",
            Amount = 130,
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            DeduplicationKey = "1"
        }, false, 6);
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
        await db.Database.MigrateAsync();
        return db;
    }
}