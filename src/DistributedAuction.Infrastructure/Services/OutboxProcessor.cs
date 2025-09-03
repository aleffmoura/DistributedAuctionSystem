using DistributedAuction.Domain.Entities;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;

namespace DistributedAuction.Infrastructure.Services;

public class OutboxProcessor(AuctionDbContext db, Func<OutboxEvent, Task> deliver)
{
    private readonly AuctionDbContext _db = db;
    private readonly Func<OutboxEvent, Task> _deliver = deliver;

    private readonly AsyncRetryPolicy _retry = Policy
        .Handle<Exception>()
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)));
    public async Task ProcessPendingAsync(int max = 100)
    {
        var pending = await _db.OutboxEvents
            .Where(o => o.ProcessedAt == null)
            .OrderBy(o => o.CreatedAt)
            .Take(max)
            .ToListAsync();

        foreach (var ev in pending)
        {
            try
            {
                await _retry.ExecuteAsync(() => _deliver(ev));
                ev.ProcessedAt = DateTime.UtcNow;
                _db.OutboxEvents.Update(ev);
                await _db.SaveChangesAsync();
            }
            catch
            {
                // retry later; do not mark processed
            }
        }
    }
}