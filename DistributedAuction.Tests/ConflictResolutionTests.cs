using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Entities;
using FluentAssertions;

namespace DistributedAuction.Tests;

public class ConflictResolutionTests
{
    [Test]
    public void ResolveSameAmountBySequenceThenTimestamp()
    {
        var r = new ConflictResolver();
        var b1 = new Bid { Amount = 100, Sequence = 2, Timestamp = DateTime.UtcNow.AddSeconds(-1) };
        var b2 = new Bid { Amount = 100, Sequence = 1, Timestamp = DateTime.UtcNow };
        var winner = r.Resolve([b1, b2]);
        winner.Should().Be(b2); // sequence lower wins
    }
}