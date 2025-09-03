using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;
using System.Collections.Concurrent;

namespace DistributedAuction.Application.Services;
public class RegionCoordinator : IRegionCoordinator
{
    private readonly ConcurrentDictionary<string, bool> _reach = new();
    public event EventHandler<PartitionEventArgs>? PartitionDetected;
    public event EventHandler<PartitionEventArgs>? PartitionHealed;

    public RegionCoordinator(params string[] regions)
    {
        foreach (var r in regions) _reach[r] = true;
    }

    public Task<bool> IsRegionReachableAsync(string region)
    {
        var ok = !_reach.TryGetValue(region, out var flag) || flag;
        return Task.FromResult(ok);
    }

    public async Task<T> ExecuteInRegionAsync<T>(string region, Func<Task<T>> operation)
    {
        if (!await IsRegionReachableAsync(region))
            throw new InvalidOperationException($"Region {region} unreachable due to partition.");
        return await operation();
    }

    public Task<PartitionStatus> GetPartitionStatusAsync()
    {
        var partitioned = _reach.Values.Any(v => v == false);
        return Task.FromResult(partitioned ? PartitionStatus.Partitioned : PartitionStatus.Healthy);
    }

    public void SimulatePartition(string regionA, string regionB)
    {
        _reach[regionA] = false;
        _reach[regionB] = false;
        PartitionDetected?.Invoke(this, new PartitionEventArgs { RegionA = regionA, RegionB = regionB });
    }

    public void HealPartition(string regionA, string regionB)
    {
        _reach[regionA] = true;
        _reach[regionB] = true;
        PartitionHealed?.Invoke(this, new PartitionEventArgs { RegionA = regionA, RegionB = regionB });
    }
}