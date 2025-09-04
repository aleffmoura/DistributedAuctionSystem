using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;

namespace DistributedAuction.Tests;

internal sealed class SingleRegionCoordinator : IRegionCoordinator
{
    public event EventHandler<PartitionEventArgs>? PartitionDetected;
    public event EventHandler<PartitionEventArgs>? PartitionHealed;
    public Task<bool> IsRegionReachableAsync(string region) => Task.FromResult(true);
    public Task<T> ExecuteInRegionAsync<T>(string region, Func<Task<T>> operation) => operation();
    public Task<PartitionStatus> GetPartitionStatusAsync() => Task.FromResult(PartitionStatus.Healthy);
}
