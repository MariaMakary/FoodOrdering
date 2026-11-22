namespace Shared.Persistence;

public abstract class AuditableEntity : IAuditable
{
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
}
