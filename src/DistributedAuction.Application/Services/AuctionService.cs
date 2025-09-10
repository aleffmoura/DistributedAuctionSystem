using DistributedAuction.Application.Mappers.Auctions;
using DistributedAuction.Domain.DomainServices.Implementations.Auctions;
using DistributedAuction.Domain.DomainServices.Implementations.Bids;
using DistributedAuction.Domain.DomainServices.Interfaces.Auctions;
using DistributedAuction.Domain.DomainServices.Interfaces.Bids;
using DistributedAuction.Domain.DomainServices.Interfaces.OutboxEvents;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    // in real world this came from constructor
    private readonly IAuctionDomainService auctionDomainService = new AuctionDomainService();
    private readonly IBidDomainService bidDomainService = new BidDomainService(sequenceService);
    private readonly IOutboxEventDomainService outboxEventDomain = new OutboxEventDomainService();

    public async Task<Auction> CreateAuctionAsync(CreateAuctionRequest auctionRequest)
    {
        var auction = auctionDomainService.Create(auctionRequest);
        auction = auctionDomainService.Configure(auction);

        await _auctionRepo.AddAsync(auction);

        _db.AuditEntries.Add(auction.CreateAuctionAuditEntry());
        await _db.SaveChangesAsync();

        return auction;
    }


    public async Task<BidResult> PlaceBidAsync(Guid auctionId, BidRequest request)
    {
        if (request.TargetRegion == request.OriginRegion)
            return await AcceptBidLocally(auctionId, request);

        if (await _region.IsRegionReachableAsync(request.TargetRegion))
        {
            return await _region.ExecuteInRegionAsync(
                request.TargetRegion,
                () => SaveBidOnRegion(auctionId, request)
            );
        }

        var pendingBid = bidDomainService.Create(auctionId, request, wasPending: true, 0);

        var payload = JsonSerializer.Serialize(pendingBid);

        _db.OutboxEvents.Add(bidDomainService.CreateOutboxEvent(pendingBid.Id, "CrossRegionBid", request.TargetRegion, payload));

        _db.AuditEntries.Add(bidDomainService.CreateAuditEntry(pendingBid.Id, "PlaceBidPendingPartition", request.OriginRegion, request.UserId, payload));

        await _db.SaveChangesAsync();
        return BidResult.Pending(pendingBid);
    }

    public Task<Auction?> GetAuctionAsync(Guid auctionId, ConsistencyLevel _)
    {
        return _auctionRepo.GetAsync(auctionId);
    }

    public async Task<ReconciliationResult> ReconcileAuctionAsync(Guid auctionId)
    {
        var auction = await _auctionRepo.GetForUpdateAsync(auctionId)
            ?? throw new InvalidOperationException("Auction not found");

        var pending = await GetAllPedingsInOthersRegion(auction);

        foreach (var ev in pending)
        {
            var bid = JsonSerializer.Deserialize<Bid>(ev.PayloadJson)!;

            if (bid.AuctionId != auctionId ||
                (auction.State == AuctionState.Ended || auction.State == AuctionState.Reconciled)
                && bid.Timestamp > auction.EndTime)
            {
                ev.Processed();
                _db.OutboxEvents.Update(ev);
                continue;
            }

            bid = await bidDomainService.UpdateSequence(bid);

            await _bidRepo.AddAsync(bid);

            auction = await auctionDomainService.UpdateIfWinner(auction, bid, _auctionRepo);

            ev.Processed();

            _db.OutboxEvents.Update(ev);

            _db.AuditEntries.Add(outboxEventDomain.CreateAuditEntry(ev, auction.Region));
        }

        var top = _resolver.Resolve(auction, _db.Bids.AsNoTracking().Where(b => b.AuctionId == auctionId));

        auction = await auctionDomainService.UpdateIfWinner(auction, top, _auctionRepo);

        auction.UpdateStateIfNecessary();

        await _auctionRepo.UpdateAsync(auction);
        await _db.SaveChangesAsync();

        return new ReconciliationResult(true, "Reconciled with pending cross-region bids.");
    }

    private async Task<List<OutboxEvent>> GetAllPedingsInOthersRegion(Auction auction)
    {
        return await _db.OutboxEvents
                    .Where(o => o.ProcessedAt == null
                             && o.DestinationRegion == auction.Region
                             && o.AggregateType == nameof(Bid)
                             && o.EventType == "CrossRegionBid")
                    .OrderBy(o => o.CreatedAt)
                    .ToListAsync();
    }

    private async Task<BidResult> SaveBidOnRegion(Guid auctionId, BidRequest request)
    {
        var result = await AcceptBidLocally(auctionId, request);

        if (result.Status == BidStatus.Accepted &&
            !string.Equals(request.TargetRegion, request.OriginRegion, StringComparison.OrdinalIgnoreCase))
        {
            var bid = result.Bid!;
            var payload = JsonSerializer.Serialize(bid);
            var outbox = bidDomainService.CreateOutboxEvent(bid.Id, "BidAccepted", request.OriginRegion, payload);

            _db.OutboxEvents.Add(outbox);
            _db.AuditEntries.Add(
                bidDomainService.CreateAuditEntry(bid.Id, "RemoteBid.Response", request.OriginRegion, request.UserId,
                JsonSerializer.Serialize(new { bid.Id, bid.Amount, bid.Sequence })));

            await _db.SaveChangesAsync();
        }

        return result;
    }
    private async Task<BidResult> AcceptBidLocally(Guid auctionId, BidRequest request)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var auction = await _auctionRepo.GetForUpdateAsync(auctionId)
            ?? throw new InvalidOperationException("Auction not found");

        if (!string.Equals(auction.Region, request.TargetRegion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Bid must be processed in the auction's owner region (target mismatch).");

        if (auction.State != AuctionState.Running)
            return BidResult.Rejected(bidDomainService.Create(auctionId, request, true, 0), "Auction not running.");

        auction.End();

        if (auction.State == AuctionState.Ended)
        {
            return await UpdateAuctionAndRejectBid(auctionId, request, tx, auction);
        }

        if (!string.IsNullOrWhiteSpace(request.DeduplicationKey))
        {
            var exists = await _db.Bids
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.AuctionId == auctionId && b.DeduplicationKey == request.DeduplicationKey);

            if (exists is not null)
                return BidResult.Accepted(exists);
        }

        var sequence = await _sequence.GetNextAsync(auctionId);

        var bid = bidDomainService.Create(auctionId, request, wasPending: false, sequence);

        var acceptance = await _ordering.ValidateBidOrderAsync(auctionId, bid);

        if (await RejectBid(tx, bid, acceptance) is { Status: BidStatus.Rejected } rejected)
        {
            return rejected;
        }

        await _bidRepo.AddAsync(bid);

        auction = await auctionDomainService.UpdateIfWinner(auction, bid, _auctionRepo);

        _db.AuditEntries.Add(
                bidDomainService.CreateAuditEntry(bid.Id, "PlaceBidAccepted", auction.Region, request.UserId,
                JsonSerializer.Serialize(new { bid.Amount, bid.Sequence, bid.OriginRegion, bid.Timestamp })));

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return BidResult.Accepted(bid);
    }

    private static async Task<BidResult?> RejectBid(IDbContextTransaction tx, Bid bid, BidAcceptance acceptance)
    {
        if (!acceptance.Accepted)
        {
            await tx.RollbackAsync();
            return BidResult.Rejected(bid, acceptance.Reason);
        }
        return null;
    }

    private async Task<BidResult> UpdateAuctionAndRejectBid(Guid auctionId, BidRequest request, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Auction auction)
    {
        await _auctionRepo.UpdateAsync(auction);
        await tx.CommitAsync();
        return BidResult.Rejected(bidDomainService.Create(auctionId, request, true, 0), "Auction already ended.");
    }
}
