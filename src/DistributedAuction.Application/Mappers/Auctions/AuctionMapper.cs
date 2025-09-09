using DistributedAuction.Domain.Entities;
using System.Text.Json;

namespace DistributedAuction.Application.Mappers.Auctions;

internal static class AuctionMapper
{
    internal static AuditEntry CreateAuctionAuditEntry(this Auction auction)
        => new()
        {
            EntityType = nameof(Auction),
            EntityId = auction.Id,
            Operation = "Create",
            Region = auction.Region,
            UserId = null,
            PayloadJson = JsonSerializer.Serialize(new { auction.Id, auction.Region, auction.StartTime, auction.EndTime })
        };
}
