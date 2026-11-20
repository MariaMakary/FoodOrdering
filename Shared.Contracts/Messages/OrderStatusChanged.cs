using Shared.Contracts.Enums;

namespace Shared.Contracts.Messages;

public record OrderStatusChanged : MessageBase
{
    public required Guid OrderId { get; init; }
    public required OrderStatus NewStatus { get; init; }
    public DateTime? EstimatedDeliveryTime { get; init; }
    public string? Reason { get; init; }
    public required DateTime ChangedAt { get; init; }
}
