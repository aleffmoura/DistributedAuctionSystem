using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;

namespace DistributedAuction.Application.Services;

public class ConflictResolver : IConflictResolver
{
    // Deterministic resolution
    // - larger Amount wins
    // - tie -> compare Sequence (lower wins)
    // - tie -> earlier Timestamp wins
    // - tie -> compare GUIDs
    public Bid Resolve(IEnumerable<Bid> bids)
    {
        if (bids == null || !bids.Any()) return null!;
        return bids
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.Sequence)
            .ThenBy(b => b.Timestamp)
            .ThenBy(b => b.Id)
            .First();
    }
}