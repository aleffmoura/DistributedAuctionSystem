using DistributedAuction.Domain.Entities;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Infrastructure.Repositories;

public class BidRepository(AuctionDbContext db)
{
    private readonly AuctionDbContext _db = db;

    public async Task AddAsync(Bid bid)
    {
        _db.Bids.Add(bid);
        await _db.SaveChangesAsync();
    }

    public async Task<int> CountForAuction(Guid auctionId)
        => await _db.Bids.CountAsync(b => b.AuctionId == auctionId);
}