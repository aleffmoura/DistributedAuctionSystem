using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace DistributedAuction.Application.Services;

public class VehicleService(IVehicleRepository repo, AuctionDbContext db) : IVehicleService
{
    private readonly IVehicleRepository _repo = repo;
    private readonly AuctionDbContext _db = db;

    public async Task<Vehicle> CreateAsync(Vehicle vehicle)
    {
        // CP write: transação forte local (região dona)
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await _repo.AddAsync(vehicle);
        await tx.CommitAsync();
        return vehicle;
    }

    public Task<Vehicle?> GetAsync(Guid id) => _repo.GetAsync(id);

    public Task<IReadOnlyList<Vehicle>> ListAsync(string region, int skip = 0, int take = 100)
        => _repo.ListByRegionAsync(region, skip, take);

    public async Task<Vehicle> UpdateAsync(Vehicle vehicle)
    {
        // valida escopo regional (não cruzar regiões)
        var exists = await _repo.ExistsInRegionAsync(vehicle.Id, vehicle.Region);
        if (!exists) throw new InvalidOperationException("Vehicle not found in this region.");
        await _repo.UpdateAsync(vehicle);
        return vehicle;
    }

    public async Task DeleteAsync(Guid id, string region)
    {
        var exists = await _repo.ExistsInRegionAsync(id, region);
        if (!exists) return;
        await _repo.DeleteAsync(id);
    }
}