using DistributedAuction.Domain.DomainServices.Interfaces.Bids;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;

namespace DistributedAuction.Domain.DomainServices.Implementations.Bids;

public class BidDomainService(IAuctionSequenceService auctionSequenceService) : IBidDomainService
{
    public Bid Create(Guid auctionId, BidRequest request, bool wasPending, long sequence)
    {
        return Bid.Create(auctionId, request.UserId, request.Amount,
           sequence, request.OriginRegion, wasPending, request.DeduplicationKey, DateTime.UtcNow);
    }


    public AuditEntry CreateAuditEntry(Guid bidId, string operation, string region, string? userId, string payload)
    {
        return new AuditEntry
        {
            EntityType = nameof(Bid),
            EntityId = bidId,
            Operation = operation,
            Region = region,
            UserId = userId,
            PayloadJson = payload
        };
    }

    public OutboxEvent CreateOutboxEvent(Guid bidId, string eventType, string region, string payload)
    {
        return new OutboxEvent
        {
            AggregateType = nameof(Bid),
            AggregateId = bidId,
            EventType = eventType,
            PayloadJson = payload,
            DestinationRegion = region
        };
    }

    public async Task<Bid> UpdateSequence(Bid bid)
    {
        var sequence = await auctionSequenceService.GetNextAsync(bid.AuctionId);

        bid.UpdateSequence(sequence);
        return bid;
    }
}
