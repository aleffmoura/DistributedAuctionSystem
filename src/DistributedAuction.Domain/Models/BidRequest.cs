namespace DistributedAuction.Domain.Models;

public class BidRequest
{
    // Region where the bidder is located
    public string OriginRegion { get; init; } = default!;
    // Region that owns the auction (where the write must land)
    public string TargetRegion { get; init; } = default!;
    public string UserId { get; init; } = default!;
    public decimal Amount { get; init; }
    // Optional id to deduplicate retries across partitions/retries
    public string? DeduplicationKey { get; init; }
}
