using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DistributedAuction.Application.Services;

public class DatabaseSyncService : IDatabaseSyncService
{
    private readonly AuctionDbContext _db;
    private readonly IAuctionService _auctionService;

    public DatabaseSyncService(AuctionDbContext db, IAuctionService auctionService)
    {
        _db = db;
        _auctionService = auctionService;
    }

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
                    // Payload é um Bid pendente; reconciliação aplicará o lance no leilão de destino.
                    var bid = JsonSerializer.Deserialize<Bid>(e.PayloadJson)!;
                    await _auctionService.ReconcileAuctionAsync(bid.AuctionId);
                    e.ProcessedAt = DateTime.UtcNow;
                    _db.OutboxEvents.Update(e);
                    break;

                case "BidAccepted":
                    // Nesso demo, o dono do leilão já aplicou o lance.
                    // Aqui apenas marcamos como entregue (em real: atualizaria caches/leitores).
                    e.ProcessedAt = DateTime.UtcNow;
                    _db.OutboxEvents.Update(e);
                    break;

                default:
                    // Eventos desconhecidos: marcar como processado para não travar fila.
                    e.ProcessedAt = DateTime.UtcNow;
                    _db.OutboxEvents.Update(e);
                    break;
            }
        }

        await _db.SaveChangesAsync(ct);
        return events.Count;
    }
}