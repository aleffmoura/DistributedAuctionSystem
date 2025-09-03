using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.Interfaces;

public interface IConflictResolver
{
    Bid Resolve(IEnumerable<Bid> bids);
}
