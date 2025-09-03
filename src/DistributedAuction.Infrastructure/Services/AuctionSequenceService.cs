using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Infrastructure.Services;

public class AuctionSequenceService(AuctionDbContext db) : IAuctionSequenceService
{
    private readonly AuctionDbContext _db = db;

    public async Task<long> GetNextAsync(Guid auctionId)
    {
        // Use transaction + SELECT FOR UPDATE to ensure atomic increment
        using var tx = await _db.Database.BeginTransactionAsync();
        var seq = await _db.AuctionSequences.SingleOrDefaultAsync(s => s.AuctionId == auctionId);
        if (seq == null)
        {
            seq = new AuctionSequence { AuctionId = auctionId, LastSequence = 1 };
            _db.AuctionSequences.Add(seq);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return 1;
        }

        // lock row
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM \"AuctionSequences\" WHERE \"AuctionId\" = {0} FOR UPDATE", auctionId);

        seq.LastSequence += 1;
        _db.AuctionSequences.Update(seq);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return seq.LastSequence;
    }
}