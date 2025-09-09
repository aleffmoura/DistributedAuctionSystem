using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Models;

namespace DistributedAuction.Domain.DomainServices.Interfaces.Bids;

public interface IBidDomainService
{
    Bid Create(Guid auctionId, BidRequest request, bool wasPending, long sequence);
    AuditEntry CreateAuditEntry(Guid bidId, string operation, string region, string? userId, string payload);
    OutboxEvent CreateOutboxEvent(Guid bidId, string eventType, string region, string payload);
    Task<Bid> UpdateSequence(Bid bid);
}