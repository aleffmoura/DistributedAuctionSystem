using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.Interfaces;

public interface IVehicleRepository
{
    Task<Vehicle?> GetAsync(Guid id);
    Task AddAsync(Vehicle vehicle);
    Task UpdateAsync(Vehicle vehicle);
    Task DeleteAsync(Guid id);
    Task<IReadOnlyList<Vehicle>> ListByRegionAsync(string region, int skip = 0, int take = 100);
    Task<bool> ExistsInRegionAsync(Guid id, string region);
}