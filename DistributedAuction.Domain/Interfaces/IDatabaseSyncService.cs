namespace DistributedAuction.Domain.Interfaces;
public interface IDatabaseSyncService
{
    Task<int> PushOutboxAsync(string destinationRegion, CancellationToken ct = default);
}