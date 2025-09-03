using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Models;

namespace DistributedAuction.Domain.Interfaces;

public interface IAuctionService
{
    Task<Auction> CreateAuctionAsync(Auction auction);
    Task<Bid> PlaceBidAsync(Guid auctionId, Bid bid);
    Task<Auction?> GetAuctionAsync(Guid auctionId, ConsistencyLevel consistency);
    Task<ReconciliationResult> ReconcileAuctionAsync(Guid auctionId);
}