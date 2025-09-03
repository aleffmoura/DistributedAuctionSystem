using DistributedAuction.Domain.Enums;

namespace DistributedAuction.Domain.Interfaces;

public class PartitionEventArgs : EventArgs
{
    public string RegionA { get; set; } = default!;
    public string RegionB { get; set; } = default!;
}

public interface IRegionCoordinator
{
    Task<bool> IsRegionReachableAsync(string region);
    Task<T> ExecuteInRegionAsync<T>(string region, Func<Task<T>> operation);
    Task<PartitionStatus> GetPartitionStatusAsync();
    event EventHandler<PartitionEventArgs>? PartitionDetected;
    event EventHandler<PartitionEventArgs>? PartitionHealed;
}