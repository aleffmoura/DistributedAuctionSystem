namespace DistributedAuction.Domain.Bases;


public class Entity<TEntity>
    where TEntity : Entity<TEntity>
{
    public Guid Id { get; protected set; }
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; protected set; } = DateTime.UtcNow;

    public virtual bool IsValid() => true;
}