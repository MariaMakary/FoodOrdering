namespace Shared.Persistence;

public interface IAuditable
{
    DateTime CreatedAt { get; }
    DateTime UpdatedAt { get; }
}
