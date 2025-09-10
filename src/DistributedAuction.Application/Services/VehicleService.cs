using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace DistributedAuction.Application.Services;

public class VehicleService(IVehicleRepository repo, AuctionDbContext db) : IVehicleService
{

    public async Task<Vehicle> CreateAsync(Vehicle vehicle)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await repo.AddAsync(vehicle);
        await tx.CommitAsync();
        return vehicle;
    }

    public Task<Vehicle?> GetAsync(Guid id) => repo.GetAsync(id);

    public Task<IReadOnlyList<Vehicle>> ListAsync(string region, int skip = 0, int take = 100)
        => repo.ListByRegionAsync(region, skip, take);

    public async Task<Vehicle> UpdateAsync(Vehicle vehicle)
    {
        var exists = await repo.ExistsInRegionAsync(vehicle.Id, vehicle.Region);
        if (!exists) throw new InvalidOperationException("Vehicle not found in this region.");
        await repo.UpdateAsync(vehicle);
        return vehicle;
    }

    public async Task DeleteAsync(Guid id, string region)
    {
        var exists = await repo.ExistsInRegionAsync(id, region);
        if (!exists) return;
        await repo.DeleteAsync(id);
    }
}