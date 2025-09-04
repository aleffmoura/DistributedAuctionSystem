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

    public async Task<Auction> CreateAuctionAsync(CreateAuctionRequest auctionRequest)
    {
        // CP: single-owner region write with strong guarantees
        var auction = new Auction
        {
            Id = Guid.NewGuid(),
            VehicleId = auctionRequest.VehicleId,
            EndTime = auctionRequest.EndTime,
            StartTime = auctionRequest.StartTime,
            Region = auctionRequest.Region,
            State = auctionRequest.State,
        };

        if (auction.StartTime == default) auction.StartTime = DateTime.UtcNow;
        if (auction.EndTime <= auction.StartTime) auction.EndTime = auction.StartTime.AddMinutes(5);
        auction.State = AuctionState.Running;

        await _auctionRepo.AddAsync(auction);

        _db.AuditEntries.Add(new AuditEntry
        {
            EntityType = nameof(Auction),
            EntityId = auction.Id,
            Operation = "Create",
            Region = auction.Region,
            UserId = null,
            PayloadJson = JsonSerializer.Serialize(new { auction.Id, auction.Region, auction.StartTime, auction.EndTime })
        });
        await _db.SaveChangesAsync();

        return auction;
    }

    public async Task<BidResult> PlaceBidAsync(Guid auctionId, BidRequest request)
    {
        // Same-region: process locally with strong consistency
        if (request.TargetRegion == request.OriginRegion)
            return await AcceptBidLocally(auctionId, request);

        // Cross-region with healthy connectivity: execute in the target (owner) region
        if (await _region.IsRegionReachableAsync(request.TargetRegion))
        {
            return await _region.ExecuteInRegionAsync(
                request.TargetRegion,
                () => AcceptBidLocally(auctionId, request)
            );
        }

        // Partition: queue as pending CrossRegionBid (availability)
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
            Operation = "PlaceBidPendingPartition",
            Region = request.OriginRegion,
            UserId = request.UserId,
            PayloadJson = payload
        });

        await _db.SaveChangesAsync();
        return BidResult.Pending(pendingBid);
    }

    public Task<Auction?> GetAuctionAsync(Guid auctionId, ConsistencyLevel _)
    {
        // No replica in this simulation -> eventual == strong
        return _auctionRepo.GetAsync(auctionId);
    }

    public async Task<ReconciliationResult> ReconcileAuctionAsync(Guid auctionId)
    {
        // Lock the auction for updates
        var auction = await _auctionRepo.GetForUpdateAsync(auctionId)
            ?? throw new InvalidOperationException("Auction not found");

        // Deliver pending cross-region bids destined to this auction's region
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

            // Skip if event not for this auction
            if (bid.AuctionId != auctionId)
            {
                ev.ProcessedAt = DateTime.UtcNow;
                _db.OutboxEvents.Update(ev);
                continue;
            }

            // If auction already ended/reconciled, discard late bids
            if ((auction.State == AuctionState.Ended || auction.State == AuctionState.Reconciled)
                && bid.Timestamp > auction.EndTime)
            {
                ev.ProcessedAt = DateTime.UtcNow;
                _db.OutboxEvents.Update(ev);
                continue;
            }

            // Assign sequence in owner region and persist (idempotent via dedup logic at service/db)
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
                EntityId = ev.Id,
                Operation = "Reconcile",
                Region = auction.Region,
                UserId = null,
                PayloadJson = ev.PayloadJson
            });
        }

        // Compute final winner deterministically (respect EndTime if ended)
        var eligible = _db.Bids.AsNoTracking().Where(b => b.AuctionId == auctionId);
        if (DateTime.UtcNow >= auction.EndTime || auction.State is AuctionState.Ended or AuctionState.Reconciled)
            eligible = eligible.Where(b => b.Timestamp <= auction.EndTime);

        var top = await eligible
            .OrderByDescending(b => (double)b.Amount)
            .ThenBy(b => b.Sequence)
            .ThenBy(b => b.Timestamp)
            .ThenBy(b => b.Id)
            .FirstOrDefaultAsync();

        if (top is not null)
        {
            auction.HighestAmount = top.Amount;
            auction.HighestBidId = top.Id;
        }

        // State transitions
        if (DateTime.UtcNow >= auction.EndTime && auction.State == AuctionState.Running)
            auction.State = AuctionState.Ended;
        if (auction.State == AuctionState.Ended)
            auction.State = AuctionState.Reconciled;

        await _auctionRepo.UpdateAsync(auction);
        await _db.SaveChangesAsync();

        return new ReconciliationResult(true, "Reconciled with pending cross-region bids.");
    }

    private async Task<BidResult> AcceptBidLocally(Guid auctionId, BidRequest request)
    {
        // Strong intra-region guarantees with serializable transaction
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var auction = await _auctionRepo.GetForUpdateAsync(auctionId)
            ?? throw new InvalidOperationException("Auction not found");

        // Ensure we're applying in the auction's owner region
        if (!string.Equals(auction.Region, request.TargetRegion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Bid must be processed in the auction's owner region (target mismatch).");

        if (auction.State != AuctionState.Running)
            return BidResult.Rejected(new Bid { AuctionId = auctionId, UserId = request.UserId, Amount = request.Amount }, "Auction not running.");

        if (DateTime.UtcNow > auction.EndTime)
        {
            auction.State = AuctionState.Ended;
            await _auctionRepo.UpdateAsync(auction);
            await tx.CommitAsync();
            return BidResult.Rejected(new Bid { AuctionId = auctionId, UserId = request.UserId, Amount = request.Amount }, "Auction already ended.");
        }

        // Idempotency by DeduplicationKey
        if (!string.IsNullOrWhiteSpace(request.DeduplicationKey))
        {
            var exists = await _db.Bids
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.AuctionId == auctionId && b.DeduplicationKey == request.DeduplicationKey);

            if (exists is not null)
                return BidResult.Accepted(exists);
        }

        var seq = await _sequence.GetNextAsync(auctionId);
        var bid = new Bid
        {
            Id = Guid.NewGuid(),
            AuctionId = auctionId,
            UserId = request.UserId,
            Amount = request.Amount,
            Sequence = seq,
            OriginRegion = request.OriginRegion,
            WasPending = false,
            DeduplicationKey = request.DeduplicationKey,
            Timestamp = DateTime.UtcNow
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

        // Propagate back to origin region (for cross-region callers) as informational event
        if (!string.Equals(request.TargetRegion, request.OriginRegion, StringComparison.OrdinalIgnoreCase))
        {
            var payload = JsonSerializer.Serialize(bid);
            _db.OutboxEvents.Add(new OutboxEvent
            {
                AggregateType = nameof(Bid),
                AggregateId = bid.Id,
                EventType = "BidAccepted",
                PayloadJson = payload,
                DestinationRegion = request.OriginRegion
            });
        }

        _db.AuditEntries.Add(new AuditEntry
        {
            EntityType = nameof(Bid),
            EntityId = bid.Id,
            Operation = "PlaceBidAccepted",
            Region = auction.Region,
            UserId = request.UserId,
            PayloadJson = JsonSerializer.Serialize(new { bid.Amount, bid.Sequence, bid.OriginRegion, bid.Timestamp })
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return BidResult.Accepted(bid);
    }
}
