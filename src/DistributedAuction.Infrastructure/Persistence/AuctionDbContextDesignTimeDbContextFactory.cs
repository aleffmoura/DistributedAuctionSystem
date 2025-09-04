using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DistributedAuction.Infrastructure.Persistence;

public class AuctionDbContextDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuctionDbContext>
{
    public AuctionDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuctionDbContext>();
        //optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=distributedauction;Username=postgres;Password=postgres");
        optionsBuilder.UseSqlite("Data Source=auction.design.db");

        return new AuctionDbContext(optionsBuilder.Options);
    }
}