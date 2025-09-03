using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.Interfaces;

public interface IBidRepository
{
    Task AddAsync(Bid bid);
    Task<int> CountForAuction(Guid auctionId);
    Task<List<Bid>> GetHistoryAsync(Guid auctionId, int take = 100, DateTime? since = null);
    Task<List<(Guid AuctionId, decimal HighestAmount)>> GetTopAuctionsAsync(string region, int topN = 10);
}