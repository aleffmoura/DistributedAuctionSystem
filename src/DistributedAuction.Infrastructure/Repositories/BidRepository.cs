using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Infrastructure.Repositories;

public class BidRepository(AuctionDbContext db) : IBidRepository
{
    private readonly AuctionDbContext _db = db;

    public async Task AddAsync(Bid bid)
    {
        _db.Bids.Add(bid);
        await _db.SaveChangesAsync();
    }
    public async Task<List<Bid>> GetHistoryAsync(Guid auctionId, int take = 100, DateTime? since = null)
    {
        return await _db.Bids.AsNoTracking()
            .Where(b => b.AuctionId == auctionId && (!since.HasValue || b.CreatedAt >= since))
            .OrderByDescending(b => b.CreatedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<List<(Guid AuctionId, decimal HighestAmount)>> GetTopAuctionsAsync(string region, int topN = 10)
    {
        return await _db.Auctions.AsNoTracking()
            .Where(a => a.Region == region && a.HighestAmount > 0)
            .OrderByDescending(a => a.HighestAmount)
            .Select(a => new ValueTuple<Guid, decimal>(a.Id, a.HighestAmount))
            .Take(topN)
            .ToListAsync();
    }
    public async Task<int> CountForAuction(Guid auctionId)
        => await _db.Bids.CountAsync(b => b.AuctionId == auctionId);
}