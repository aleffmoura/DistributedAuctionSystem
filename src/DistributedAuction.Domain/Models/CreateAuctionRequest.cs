using DistributedAuction.Domain.Enums;

namespace DistributedAuction.Domain.Models;
public record CreateAuctionRequest
{
    public Guid VehicleId { get; set; }
    public string Region { get; set; } = default!;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public AuctionState State { get; set; }
}