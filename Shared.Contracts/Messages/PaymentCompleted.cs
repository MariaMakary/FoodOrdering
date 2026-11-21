using Shared.Contracts.Enums;

namespace Shared.Contracts.Messages;

public record PaymentCompleted : MessageBase
{
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required PaymentMethod Method { get; init; }
    public required DateTime PaidAt { get; init; }
}
