using DistributedAuction.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Infrastructure.Persistence;

public class AuctionDbContext(DbContextOptions<AuctionDbContext> opts) : DbContext(opts)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<AuctionSequence> AuctionSequences => Set<AuctionSequence>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vehicle>().HasDiscriminator<string>("VehicleType")
            .HasValue<Sedan>("Sedan")
            .HasValue<Suv>("SUV")
            .HasValue<Hatchback>("Hatchback")
            .HasValue<Truck>("Truck");

        modelBuilder.Entity<AuctionSequence>().HasKey(s => s.AuctionId);

        modelBuilder.Entity<Bid>()
            .HasIndex(b => new { b.AuctionId, b.Sequence })
            .IsUnique();

        modelBuilder.Entity<Bid>()
            .HasIndex(b => new { b.AuctionId, b.Amount });
        
        modelBuilder.Entity<Auction>()
            .Property(a => a.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken()
            .HasDefaultValue(new byte[8]);

        //makes EF use a DB concurrency token. On Postgres we used a bytea timestamp column created by EF.
    }
}