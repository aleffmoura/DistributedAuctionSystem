using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DistributedAuction.Application.Services;

public class DatabaseSyncService(AuctionDbContext db, IAuctionService auctionService) : IDatabaseSyncService
{
    private readonly AuctionDbContext _db = db;
    private readonly IAuctionService _auctionService = auctionService;

    public async Task<int> PushOutboxAsync(string destinationRegion, CancellationToken ct = default)
    {
        var events = await _db.OutboxEvents
            .Where(e => e.ProcessedAt == null && e.DestinationRegion == destinationRegion)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        foreach (var e in events)
        {
            switch (e.EventType)
            {
                case "CrossRegionBid":
                    var bid = JsonSerializer.Deserialize<Bid>(e.PayloadJson)!;
                    await _auctionService.ReconcileAuctionAsync(bid.AuctionId);
                    e.ProcessedAt = DateTime.UtcNow;
                    _db.OutboxEvents.Update(e);
                    break;

                case "BidAccepted":
                    e.ProcessedAt = DateTime.UtcNow;
                    _db.OutboxEvents.Update(e);
                    break;

                default:
                    e.ProcessedAt = DateTime.UtcNow;
                    _db.OutboxEvents.Update(e);
                    break;
            }
        }

        await _db.SaveChangesAsync(ct);
        return events.Count;
    }
}