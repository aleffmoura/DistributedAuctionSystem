using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;

namespace DistributedAuction.Tests.Commons;

internal sealed class HealthyDualRegionCoordinator(params string[] regions) : IRegionCoordinator
{
    private readonly HashSet<string> _regions = [.. regions];

    public event EventHandler<PartitionEventArgs>? PartitionDetected;
    public event EventHandler<PartitionEventArgs>? PartitionHealed;
    public Task<bool> IsRegionReachableAsync(string region) => Task.FromResult(_regions.Contains(region));
    public Task<T> ExecuteInRegionAsync<T>(string region, Func<Task<T>> operation)
    {
        if (!_regions.Contains(region)) throw new InvalidOperationException("Unreachable");
        return operation();
    }
    public Task<PartitionStatus> GetPartitionStatusAsync() => Task.FromResult(PartitionStatus.Healthy);
}

