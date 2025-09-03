using DistributedAuction.Domain.Entities;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Infrastructure.Services;

public class OutboxProcessor(AuctionDbContext db, Func<OutboxEvent, Task> deliver)
{
    private readonly AuctionDbContext _db = db;
    private readonly Func<OutboxEvent, Task> _deliver = deliver;

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
                await _deliver(ev); // deliver to destination region (simulated)
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