using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Application.Services;

public class BidOrderingService : IBidOrderingService
{
    private readonly IAuctionSequenceService _sequence;
    private readonly AuctionDbContext _db;

    public BidOrderingService(IAuctionSequenceService sequence, AuctionDbContext db)
    {
        _sequence = sequence;
        _db = db;
    }

    public Task<long> GetNextBidSequenceAsync(Guid auctionId)
        => _sequence.GetNextAsync(auctionId);

    public async Task<BidAcceptance> ValidateBidOrderAsync(Guid auctionId, Bid bid)
    {
        // 1) Sequência monotônica (não precisa ser consecutiva, mas deve ser estritamente maior)
        var lastSeq = await _db.Bids
            .Where(b => b.AuctionId == auctionId)
            .Select(b => (long?)b.Sequence)
            .OrderByDescending(s => s)
            .FirstOrDefaultAsync() ?? 0;

        if (bid.Sequence <= lastSeq)
            return new BidAcceptance(false, $"Sequence must be greater than {lastSeq}.");

        // 2) Ascendência de valor (English auction)
        var highest = await _db.Auctions
            .Where(a => a.Id == auctionId)
            .Select(a => a.HighestAmount)
            .FirstAsync();

        if (bid.Amount <= highest)
            return new BidAcceptance(false, $"Bid must be greater than current highest ({highest}).");

        return new BidAcceptance(true);
    }

    public async Task<IEnumerable<Bid>> GetOrderedBidsAsync(Guid auctionId, DateTime? since = null)
    {
        return await _db.Bids.AsNoTracking()
           .Where(b => b.AuctionId == auctionId && (!since.HasValue || b.CreatedAt >= since))
           .OrderBy(b => b.Sequence)
           .ThenBy(b => b.CreatedAt)
           .ThenBy(b => b.Id)
           .ToListAsync();
    }
}