using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;

public class OptimisticConcurrencyTests
{
    private static DbContextOptions<AuctionDbContext> BuildOptions(SqliteConnection conn) =>
        new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(conn)
            .EnableSensitiveDataLogging()
            .Options;

    [Test]
    public async Task Updating_Auction_From_Two_Contexts_Should_Throw_DbUpdateConcurrencyException()
    {
        // 1) Uma única conexão SQLite in-memory compartilhada entre 2 DbContexts
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var options = BuildOptions(conn);

        // 2) Cria schema e semeia um leilão com RowVersion inicial
        await using (var seed = new AuctionDbContext(options))
        {
            await seed.Database.MigrateAsync();

            var auction = new Auction
            {
                Id = Guid.NewGuid(),
                VehicleId = Guid.NewGuid(),
                Region = "US-East",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(10),
                State = AuctionState.Running,
                HighestAmount = 0m
            };

            seed.Auctions.Add(auction);
            await seed.SaveChangesAsync();
        }

        await using var db1 = new AuctionDbContext(options);
        await using var db2 = new AuctionDbContext(options);

        var a1 = await db1.Auctions.SingleAsync();
        var a2 = await db2.Auctions.SingleAsync();

        a1.HighestAmount = 100m;
        await db1.SaveChangesAsync();

        a2.HighestAmount = 120m;

        Func<Task> act = async () => await db2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await db2.Entry(a2).ReloadAsync();
        a2.HighestAmount.Should().Be(100m);
    }
}