using DistributedAuction.Domain.Interfaces;

namespace DistributedAuction.Tests;

public sealed class FakeSequenceService : IAuctionSequenceService
{
    private readonly Dictionary<Guid, long> _seq = new();
    public Task<long> GetNextAsync(Guid auctionId)
    {
        if (!_seq.TryGetValue(auctionId, out var v)) v = 0;
        v += 1;
        _seq[auctionId] = v;
        return Task.FromResult(v);
    }
}