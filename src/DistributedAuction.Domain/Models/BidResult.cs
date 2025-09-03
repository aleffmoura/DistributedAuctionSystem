
using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.Models;

public enum BidStatus { Accepted, Rejected, PendingPartition }

public class BidResult
{
    public BidStatus Status { get; }
    public Bid? Bid { get; }
    public string? Reason { get; }

    private BidResult(BidStatus status, Bid? bid = null, string? reason = null)
    {
        Status = status;
        Bid = bid;
        Reason = reason;
    }

    public static BidResult Accepted(Bid bid) => new(BidStatus.Accepted, bid);
    public static BidResult Rejected(Bid bid, string reason) => new(BidStatus.Rejected, bid, reason);
    public static BidResult Pending(Bid bid) => new(BidStatus.PendingPartition, bid);
}
