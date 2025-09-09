using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.DomainServices.Interfaces.OutboxEvents;

public interface IOutboxEventDomainService
{
    AuditEntry CreateAuditEntry(OutboxEvent ev, string region);
}
