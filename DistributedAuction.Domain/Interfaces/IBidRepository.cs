using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.Interfaces;

public interface IBidRepository
{
    Task AddAsync(Bid bid);
    Task<int> CountForAuction(Guid auctionId);
}