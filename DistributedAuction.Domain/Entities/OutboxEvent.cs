namespace DistributedAuction.Domain.Entities;

public class OutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AggregateType { get; set; } = default!; // e.g., "Bid"
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = default!;
    public string PayloadJson { get; set; } = default!;
    public string DestinationRegion { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}