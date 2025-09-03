using DistributedAuction.Domain.Entities;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Infrastructure.Repositories;

public class AuctionRepository(AuctionDbContext db)
{
    private readonly AuctionDbContext _db = db;

    public async Task<Auction?> GetAsync(Guid id) =>
        await _db.Auctions.Include(a => a.Bids).SingleOrDefaultAsync(a => a.Id == id);

    public async Task<Auction?> GetForUpdateAsync(Guid id) =>
        await _db.Auctions.Include(a => a.Bids).SingleOrDefaultAsync(a => a.Id == id);

    public async Task AddAsync(Auction auction)
    {
        _db.Auctions.Add(auction);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Auction auction)
    {
        _db.Auctions.Update(auction);
        await _db.SaveChangesAsync();
    }
}