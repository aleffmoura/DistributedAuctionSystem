using DistributedAuction.Domain.DomainServices.Interfaces.OutboxEvents;
using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.DomainServices.Implementations.Auctions;

public class OutboxEventDomainService : IOutboxEventDomainService
{
    public AuditEntry CreateAuditEntry(OutboxEvent ev, string region)
    {
        return new AuditEntry
        {
            EntityType = nameof(Domain.Entities.OutboxEvent),
            EntityId = ev.Id,
            Operation = "Reconcile",
            Region = region,
            UserId = null,
            PayloadJson = ev.PayloadJson
        };
    }
}
