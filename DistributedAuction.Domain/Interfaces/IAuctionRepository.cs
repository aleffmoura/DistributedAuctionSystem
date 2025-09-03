using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.Interfaces;


public interface IAuctionRepository
{
    Task AddAsync(Auction auction);
    Task<Auction?> GetAsync(Guid auctionId);
    Task<Auction?> GetForUpdateAsync(Guid auctionId);
    Task UpdateAsync(Auction auction);
}