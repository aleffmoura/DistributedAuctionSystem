namespace DistributedAuction.Domain.Entities;

public abstract class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Make { get; set; } = default!;
    public string Model { get; set; } = default!;
    public int Year { get; set; }
    public string Region { get; set; } = default!; // e.g. "US-East" or "EU-West"
}

public class Sedan : Vehicle { }
public class Suv : Vehicle { }
public class Hatchback : Vehicle { }
public class Truck : Vehicle { }