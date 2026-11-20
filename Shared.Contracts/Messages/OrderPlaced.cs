using Shared.Contracts.Enums;

namespace Shared.Contracts.Messages;

public record OrderPlaced : MessageBase
{
    public required Guid OrderId { get; init; }
    public required string OrderNumber { get; init; }
    public required Guid CustomerId { get; init; }
    public required string CustomerName { get; init; }
    public required string CustomerPhone { get; init; }
    public required DeliveryType DeliveryType { get; init; }
    public string? Address { get; init; }
    public string? Notes { get; init; }
    public required PaymentMethod PaymentMethod { get; init; }
    public required IReadOnlyList<OrderItemDto> Items { get; init; }
    public required decimal Total { get; init; }
    public required DateTime PlacedAt { get; init; }
}
