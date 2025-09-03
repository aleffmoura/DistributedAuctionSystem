using DistributedAuction.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace DistributedAuction.Domain.Entities;

public class Auction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public string Region { get; set; } = default!; // owner region
    public AuctionState State { get; set; } = AuctionState.Created;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    public decimal HighestAmount { get; set; }
    public Guid? HighestBidId { get; set; }

    public List<Bid> Bids { get; set; } = [];
    public long Version { get; set; }
}