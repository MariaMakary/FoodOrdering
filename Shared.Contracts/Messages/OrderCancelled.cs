namespace Shared.Contracts.Messages;

public record OrderCancelled : MessageBase
{
    public required Guid OrderId { get; init; }
    public string? Reason { get; init; }
    public required DateTime CancelledAt { get; init; }
}
