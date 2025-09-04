using DistributedAuction.Domain.Interfaces;

namespace DistributedAuction.Tests.Commons;

public class FakeSequenceService : IAuctionSequenceService
{
    private long _last;
    private readonly object _gate = new();

    public Task<long> GetNextAsync(Guid auctionId)
    {
        lock (_gate)
        {
            _last++;
            return Task.FromResult(_last);
        }
    }
}