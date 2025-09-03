using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace DistributedAuction.Application.Services;

public class AuctionService(
    IAuctionRepository auctionRepo,
    IBidRepository bidRepo,
    IAuctionSequenceService sequenceService,
    IBidOrderingService orderingService,
    IRegionCoordinator regionCoordinator,
    IConflictResolver conflictResolver,
    AuctionDbContext db) : IAuctionService
{
    private readonly IAuctionRepository _auctionRepo = auctionRepo;
    private readonly IBidRepository _bidRepo = bidRepo;
    private readonly IBidOrderingService _ordering = orderingService;
    private readonly IAuctionSequenceService _sequence = sequenceService;
    private readonly IRegionCoordinator _region = regionCoordinator;
    private readonly IConflictResolver _resolver = conflictResolver;
    private readonly AuctionDbContext _db = db;

    public async Task<Auction> CreateAuctionAsync(Auction auction)
    {
        // CP choice: write to single owner region with strong constraints
        if (auction.StartTime == default) auction.StartTime = DateTime.UtcNow;
        if (auction.EndTime <= auction.StartTime) auction.EndTime = auction.StartTime.AddMinutes(5);
        auction.State = AuctionState.Running;
        await _auctionRepo.AddAsync(auction);
        return auction;
    }

    public async Task<BidResult> PlaceBidAsync(Guid auctionId, BidRequest request)
    {
        if (request.TargetRegion == request.OriginRegion)
        {
            // local strong consistency
            return await AcceptBidLocally(auctionId, request);
        }
        else
        {
            if (await _region.IsRegionReachableAsync(request.TargetRegion))
            {
                return await AcceptBidLocally(auctionId, request);
            }
            else
            {
                var pendingBid = new Bid
                {
                    Id = Guid.NewGuid(),
                    AuctionId = auctionId,
                    UserId = request.UserId,
                    Amount = request.Amount,
                    OriginRegion = request.OriginRegion,
                    WasPending = true,
                    DeduplicationKey = request.DeduplicationKey ?? Guid.NewGuid().ToString(),
                    Timestamp = DateTime.UtcNow
                };

                var payload = JsonSerializer.Serialize(pendingBid);
                _db.OutboxEvents.Add(new OutboxEvent
                {
                    AggregateType = nameof(Bid),
                    AggregateId = pendingBid.Id,
                    EventType = "CrossRegionBid",
                    PayloadJson = payload,
                    DestinationRegion = request.TargetRegion
                });

                _db.AuditEntries.Add(new AuditEntry
                {
                    EntityType = nameof(Bid),
                    EntityId = pendingBid.Id,
                    Operation = "PlaceBid",
                    Region = request.OriginRegion,
                    UserId = null,
                    PayloadJson = JsonSerializer.Serialize(pendingBid)
                });
                await _db.SaveChangesAsync();
                return BidResult.Pending(pendingBid);
            }
        }
    }

    public async Task<Auction?> GetAuctionAsync(Guid auctionId, ConsistencyLevel consistency)
    {
        if (consistency == ConsistencyLevel.Eventual)
        {
            await _auctionRepo.GetAsync(auctionId);
        }
        return await _auctionRepo.GetAsync(auctionId);
    }

    public async Task<ReconciliationResult> ReconcileAuctionAsync(Guid auctionId)
    {
        var auction = await _auctionRepo.GetForUpdateAsync(auctionId)
            ?? throw new InvalidOperationException("Auction not found");

        var pending = await _db.OutboxEvents
                .Where(o => o.ProcessedAt == null
                         && o.DestinationRegion == auction.Region
                         && o.AggregateType == nameof(Bid)
                         && o.EventType == "CrossRegionBid")
                .OrderBy(o => o.CreatedAt)
                .ToListAsync();

        foreach (var ev in pending)
        {
            var bid = JsonSerializer.Deserialize<Bid>(ev.PayloadJson)!;
            if (bid.AuctionId != auctionId) { ev.ProcessedAt = DateTime.UtcNow; continue; }

            if ((auction.State == AuctionState.Ended || auction.State == AuctionState.Reconciled)
                && bid.Timestamp > auction.EndTime)
            {
                ev.ProcessedAt = DateTime.UtcNow;
                continue;
            }

            bid.Sequence = await _sequence.GetNextAsync(auctionId);
            await _bidRepo.AddAsync(bid);

            if (bid.Amount > auction.HighestAmount)
            {
                auction.HighestAmount = bid.Amount;
                auction.HighestBidId = bid.Id;
                await _auctionRepo.UpdateAsync(auction);
            }

            ev.ProcessedAt = DateTime.UtcNow;
            _db.OutboxEvents.Update(ev);
            _db.AuditEntries.Add(new AuditEntry
            {
                EntityType = nameof(OutboxEvent),
                EntityId = auction.Id,
                Operation = "Reconcile",
                Region = auction.Region,
                UserId = null,
                PayloadJson = JsonSerializer.Serialize(auction)
            });
        }

        var eligible = _db.Bids.AsNoTracking()
            .Where(b => b.AuctionId == auctionId);

        if (DateTime.UtcNow >= auction.EndTime || auction.State is AuctionState.Ended or AuctionState.Reconciled)
            eligible = eligible.Where(b => b.Timestamp <= auction.EndTime);

        var topAmount = await eligible.Select(b => (decimal?)b.Amount).MaxAsync() ?? 0m;

        if (topAmount > 0)
        {
            var winner = await eligible
                .Where(b => b.Amount == topAmount)
                .OrderBy(b => b.Sequence)  
                .ThenBy(b => b.CreatedAt)  
                .ThenBy(b => b.Id)         
                .FirstAsync();

            auction.HighestAmount = winner.Amount;
            auction.HighestBidId = winner.Id;
            _db.Auctions.Update(auction);
        }

        if (DateTime.UtcNow >= auction.EndTime && auction.State == AuctionState.Running)
            auction.State = AuctionState.Ended;
        if (auction.State == AuctionState.Ended)
            auction.State = AuctionState.Reconciled;

        await _auctionRepo.UpdateAsync(auction);
        return new ReconciliationResult(true, "Reconciled with pending cross-region bids.");
    }

    private async Task<BidResult> AcceptBidLocally(Guid auctionId, BidRequest request)
    {
        // Serializable to provide strong intra-region ordering guarantees
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var auction = await _auctionRepo.GetForUpdateAsync(auctionId) ?? throw new InvalidOperationException("Auction not found");
        if (auction.State != AuctionState.Running)
            return BidResult.Rejected(new Bid { AuctionId = auctionId, UserId = request.UserId, Amount = request.Amount }, "Auction not running.");

        if (DateTime.UtcNow > auction.EndTime)
        {
            auction.State = AuctionState.Ended;
            await _auctionRepo.UpdateAsync(auction);
            await tx.CommitAsync();
            return BidResult.Rejected(new Bid { AuctionId = auctionId, UserId = request.UserId, Amount = request.Amount }, "Auction already ended.");
        }

        if (!string.IsNullOrWhiteSpace(request.DeduplicationKey))
        {
            var exists = await _db.Bids
                .AsNoTracking()
                .FirstOrDefaultAsync(b =>
                    b.AuctionId == auctionId &&
                    b.DeduplicationKey == request.DeduplicationKey);

            if (exists is not null)
                return BidResult.Accepted(exists);
        }
        var seq = await _sequence.GetNextAsync(auctionId);
        var bid = new Bid
        {
            AuctionId = auctionId,
            UserId = request.UserId,
            Amount = request.Amount,
            Sequence = seq,
            OriginRegion = request.OriginRegion,
            WasPending = false,
            DeduplicationKey = request.DeduplicationKey
        };

        var acceptance = await _ordering.ValidateBidOrderAsync(auctionId, bid);
        if (!acceptance.Accepted)
        {
            await tx.RollbackAsync();
            return BidResult.Rejected(bid, acceptance.Reason);
        }

        await _bidRepo.AddAsync(bid);

        if (bid.Amount > auction.HighestAmount)
        {
            auction.HighestAmount = bid.Amount;
            auction.HighestBidId = bid.Id;
            await _auctionRepo.UpdateAsync(auction);
        }

        var otherRegion = request.TargetRegion;
        var payload = JsonSerializer.Serialize(bid);
        _db.OutboxEvents.Add(new OutboxEvent
        {
            AggregateType = nameof(Bid),
            AggregateId = bid.Id,
            EventType = "BidAccepted",
            PayloadJson = payload,
            DestinationRegion = otherRegion
        });
        await _db.SaveChangesAsync();
        _db.AuditEntries.Add(new AuditEntry
        {
            EntityType = nameof(Auction),
            EntityId = auction.Id,
            Operation = "Create",
            Region = auction.Region,
            UserId = null,
            PayloadJson = JsonSerializer.Serialize(auction)
        });
        await tx.CommitAsync();
        return BidResult.Accepted(bid);
    }
}
