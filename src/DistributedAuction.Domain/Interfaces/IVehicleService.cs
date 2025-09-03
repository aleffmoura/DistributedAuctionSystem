using DistributedAuction.Domain.Entities;

namespace DistributedAuction.Domain.Interfaces;

public interface IVehicleService
{
    // CP writes: apenas na região do veículo
    Task<Vehicle> CreateAsync(Vehicle vehicle);
    Task<Vehicle?> GetAsync(Guid id); // leitura forte local
    Task<IReadOnlyList<Vehicle>> ListAsync(string region, int skip = 0, int take = 100);
    Task<Vehicle> UpdateAsync(Vehicle vehicle);
    Task DeleteAsync(Guid id, string region);
}