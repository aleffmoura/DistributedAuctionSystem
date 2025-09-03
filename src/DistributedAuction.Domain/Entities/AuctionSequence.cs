namespace DistributedAuction.Domain.Entities;

public class AuctionSequence
{
    public Guid AuctionId { get; set; }
    public long LastSequence { get; set; }
}