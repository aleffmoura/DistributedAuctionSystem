using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;

namespace DistributedAuction.Application.Services;

public class ConflictResolver : IConflictResolver
{
    public Bid Resolve(Auction auction, IEnumerable<Bid> bids)
    {
        if (bids == null || !bids.Any()) return null!;

        if (DateTime.UtcNow >= auction.EndTime || auction.State is AuctionState.Ended or AuctionState.Reconciled)
            bids = bids.Where(b => b.Timestamp <= auction.EndTime);

        return bids
            .OrderByDescending(b => (double)b.Amount)
            .ThenBy(b => b.Sequence)
            .ThenBy(b => b.Timestamp)
            .ThenBy(b => b.Id)
            .First();
    }
}