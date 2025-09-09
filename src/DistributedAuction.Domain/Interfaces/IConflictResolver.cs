using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.Interfaces;

public interface IConflictResolver
{
    Bid Resolve(Auction auction, IEnumerable<Bid> bids);
}
