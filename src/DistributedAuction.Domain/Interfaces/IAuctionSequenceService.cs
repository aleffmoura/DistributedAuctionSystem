namespace DistributedAuction.Domain.Interfaces;

public interface IAuctionSequenceService
{
    Task<long> GetNextAsync(Guid auctionId);
}