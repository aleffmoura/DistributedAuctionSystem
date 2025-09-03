using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Infrastructure.Repositories;

public class VehicleRepository(AuctionDbContext db) : IVehicleRepository
{
    private readonly AuctionDbContext _db = db;

    public async Task<Vehicle?> GetAsync(Guid id)
        => await _db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id);

    public async Task AddAsync(Vehicle vehicle)
    {
        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Vehicle vehicle)
    {
        _db.Vehicles.Update(vehicle);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var v = await _db.Vehicles.FindAsync(id);
        if (v is null) return;
        _db.Vehicles.Remove(v);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Vehicle>> ListByRegionAsync(string region, int skip = 0, int take = 100)
        => await _db.Vehicles.AsNoTracking()
              .Where(v => v.Region == region)
              .OrderBy(v => v.Id)
              .Skip(skip).Take(take).ToListAsync();

    public async Task<bool> ExistsInRegionAsync(Guid id, string region)
        => await _db.Vehicles.AsNoTracking()
              .AnyAsync(v => v.Id == id && v.Region == region);
}