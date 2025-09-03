using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace DistributedAuction.Infrastructure.Services;

public class AuctionSequenceService(AuctionDbContext db) : IAuctionSequenceService
{
    private readonly AuctionDbContext _db = db;

    public async Task<long> GetNextAsync(Guid auctionId)
    {
        // Se já há uma transação ativa (ex.: AcceptBidLocally), NÃO começamos outra.
        var hasAmbientTx = _db.Database.CurrentTransaction is not null;

        // Abrimos transação própria SÓ se necessário (fora de um fluxo transacional).
        IDbContextTransaction? localTx = null;
        if (!hasAmbientTx)
        {
            localTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        }

        try
        {
            // Carrega ou cria o registro de sequência do leilão
            var seq = await _db.AuctionSequences
                .SingleOrDefaultAsync(x => x.AuctionId == auctionId);

            if (seq is null)
            {
                seq = new AuctionSequence
                {
                    AuctionId = auctionId,
                    LastSequence = 1
                };
                _db.AuctionSequences.Add(seq);
            }
            else
            {
                seq.LastSequence += 1;
                _db.AuctionSequences.Update(seq);
            }

            await _db.SaveChangesAsync();

            if (localTx is not null)
                await localTx.CommitAsync();

            return seq.LastSequence;
        }
        catch
        {
            if (localTx is not null)
                await localTx.RollbackAsync();
            throw;
        }
        finally
        {
            if (localTx is not null)
                await localTx.DisposeAsync();
        }
    }
}