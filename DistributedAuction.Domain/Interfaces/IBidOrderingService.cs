using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Models;

namespace DistributedAuction.Domain.Interfaces;

public interface IBidOrderingService
{
    Task<long> GetNextBidSequenceAsync(Guid auctionId);
    Task<BidAcceptance> ValidateBidOrderAsync(Guid auctionId, Bid bid);
    Task<IEnumerable<Bid>> GetOrderedBidsAsync(Guid auctionId, DateTime? since = null);
}