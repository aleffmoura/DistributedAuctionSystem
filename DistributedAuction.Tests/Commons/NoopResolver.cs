using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;

namespace DistributedAuction.Tests.Commons;

internal sealed class NoopResolver : IConflictResolver
{
    public Bid Resolve(IEnumerable<Bid> bids)
        => bids.OrderByDescending(b => b.Amount).ThenBy(b => b.Sequence).First();
}
