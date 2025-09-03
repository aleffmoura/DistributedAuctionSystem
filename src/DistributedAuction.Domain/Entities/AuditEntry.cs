namespace DistributedAuction.Domain.Entities;

public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public string EntityType { get; set; } = default!;
    public Guid? EntityId { get; set; }
    public string Operation { get; set; } = default!;
    public string Region { get; set; } = default!;
    public string? UserId { get; set; }
    public string PayloadJson { get; set; } = "{}";
}