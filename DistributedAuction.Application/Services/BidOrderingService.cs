using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Application.Services;

public class BidOrderingService : IBidOrderingService
{
    private readonly IAuctionSequenceService _sequenceService;
    private readonly AuctionDbContext _db;

    public BidOrderingService(IAuctionSequenceService sequenceService, AuctionDbContext db)
    {
        _sequenceService = sequenceService;
        _db = db;
    }

    public Task<long> GetNextBidSequenceAsync(Guid auctionId)
        => _sequenceService.GetNextAsync(auctionId);

    public async Task<BidAcceptance> ValidateBidOrderAsync(Guid auctionId, Bid bid)
    {
        // Ascending-only: new amount must be > current highest
        var highest = await _db.Auctions.Where(a => a.Id == auctionId)
                                        .Select(a => a.HighestAmount)
                                        .SingleAsync();
        if (bid.Amount <= highest)
            return new BidAcceptance(false, $"Bid amount {bid.Amount} must be greater than current highest {highest}.");
        return new BidAcceptance(true);
    }

    public async Task<IEnumerable<Bid>> GetOrderedBidsAsync(Guid auctionId, DateTime? since = null)
    {
        var q = _db.Bids.AsNoTracking().Where(b => b.AuctionId == auctionId);
        if (since is DateTime t) q = q.Where(b => b.Timestamp >= t);
        return await q.OrderBy(b => b.Sequence).ToListAsync();
    }
}
