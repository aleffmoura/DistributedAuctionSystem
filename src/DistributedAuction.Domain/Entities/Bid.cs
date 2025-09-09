using DistributedAuction.Domain.Bases;
using DistributedAuction.Domain.Enums;

namespace DistributedAuction.Domain.Entities;

public class Bid : Entity<Bid>
{
    public Guid AuctionId { get; set; }
    public string UserId { get; set; } = default!;
    public decimal Amount { get; set; }
    public long Sequence { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string OriginRegion { get; set; } = default!;
    public bool WasPending { get; set; } = false;
    public string? DeduplicationKey { get; set; }
    internal void UpdateSequence(long sequence) => this.Sequence = sequence;

    internal static Bid Create(Guid auctionId, string userId, decimal amount) => new()
    { AuctionId = auctionId, UserId = userId, Amount = amount };

    internal static Bid Create(
            Guid auctionId,
            string userId,
            decimal amount,
            long sequence,
            string origin,
            bool wasPending,
            string? deduplicationKey,
            DateTime timestamp) => new()
            {
                Id = Guid.NewGuid(),
                AuctionId = auctionId,
                UserId = userId,
                Amount = amount,
                Sequence = sequence,
                OriginRegion = origin,
                WasPending = wasPending,
                DeduplicationKey = string.IsNullOrWhiteSpace(deduplicationKey)
                ? Guid.NewGuid().ToString()
                : deduplicationKey,
                Timestamp = timestamp
            };
}