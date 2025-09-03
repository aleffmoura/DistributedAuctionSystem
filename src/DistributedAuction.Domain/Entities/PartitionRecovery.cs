namespace DistributedAuction.Domain.Entities;

public class PartitionRecovery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuctionId { get; set; }
    public string Region { get; set; } = default!;
    public DateTime LastProcessedEventAt { get; set; } = DateTime.MinValue;
}