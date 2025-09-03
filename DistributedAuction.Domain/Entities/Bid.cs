namespace DistributedAuction.Domain.Entities;

public class Bid
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuctionId { get; set; }
    public string UserId { get; set; } = default!;
    public decimal Amount { get; set; }
    public long Sequence { get; set; } // ordering token
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // origin region helps dedupe & tie-break
    public string OriginRegion { get; set; } = default!;
    public bool WasPending { get; set; } = false; // true if accepted locally but awaiting cross-region sync
    public string? DeduplicationKey { get; set; } // optional
}