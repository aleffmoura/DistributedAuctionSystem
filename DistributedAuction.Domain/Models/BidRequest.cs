namespace DistributedAuction.Domain.Models;

public class BidRequest
{
    public string Region { get; set; }
    public string TargetRegion { get; set; }
    public object UserId { get; set; }
    public object Amount { get; set; }
}
