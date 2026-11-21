namespace Shared.Contracts.Messages;

public abstract record MessageBase
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public Guid? CorrelationId { get; init; }
}
