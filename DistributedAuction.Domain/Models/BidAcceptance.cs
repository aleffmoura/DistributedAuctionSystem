namespace DistributedAuction.Domain.Models;
public record BidAcceptance(bool Accepted, string Reason = "", Guid? BidId = null);