using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using FluentAssertions;

namespace DistributedAuction.Tests;

public class AuctionStateMachineTests
{
    [Test]
    public void CanStartAndEndAuction()
    {
        var a = new Auction
        {
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(5),
            State = AuctionState.Created
        };
        a.State = AuctionState.Running;
        a.State.Should().Be(AuctionState.Running);
        a.State = AuctionState.Ended;
        a.State.Should().Be(AuctionState.Ended);
    }
}