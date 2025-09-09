using DistributedAuction.Domain.Bases;
using DistributedAuction.Domain.Enums;
using System.Security.Cryptography;

namespace DistributedAuction.Domain.Entities;

public class Auction : Entity<Auction>
{
    public Guid VehicleId { get; private set; }
    public string Region { get; private set; } = default!;
    public AuctionState State { get; private set; } = AuctionState.Created;
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }

    public decimal HighestAmount { get; private set; }
    public Guid? HighestBidId { get; private set; }

    public List<Bid> Bids { get; init; } = [];
    public long Version { get; private set; }

    public void UpdateStart(DateTime startTime) => this.StartTime = startTime;
    public void UpdateEnd(DateTime startTime) => this.EndTime = startTime;
    public void UpdateStateIfNecessary()
    {
        if (DateTime.UtcNow >= this.EndTime && this.State == AuctionState.Running)
            this.State = AuctionState.Ended;
        if (this.State == AuctionState.Ended)
            this.State = AuctionState.Reconciled;
    }
    internal void UpdateWinner(Guid bidId, decimal amount)
    {
        this.HighestBidId = bidId;
        this.HighestAmount = amount;
    }

    public void Start()
    {
        if (this.StartTime == default)
            this.StartTime = DateTime.UtcNow;

        this.State = AuctionState.Running;
    }

    public void End()
    {
        if (DateTime.UtcNow > this.EndTime)
            this.State = AuctionState.Ended;
    }

    internal static Auction Create(
            Guid VehicleId,
            DateTime EndTime,
            DateTime StartTime,
            string Region) => new()
            {
                Id = Guid.NewGuid(),
                VehicleId = VehicleId,
                Region = Region,
                State = AuctionState.Created,
                StartTime = StartTime,
                EndTime = EndTime,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
}