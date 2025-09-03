using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;
public class BidOrderingTests
{
    [Test]
    public async Task SequencesAreMonotonicUnderConcurrency()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new AuctionDbContext(options);
        var seqService = new AuctionSequenceService(db);
        var auctionId = Guid.NewGuid();

        var tasks = Enumerable.Range(0, 50).Select(async _ => await seqService.GetNextAsync(auctionId)).ToArray();
        await Task.WhenAll(tasks);
        var results = tasks.Select(t => t.Result).OrderBy(x => x).ToList();
        results.Should().BeInAscendingOrder();
        results.Count.Should().Be(50);
    }
}