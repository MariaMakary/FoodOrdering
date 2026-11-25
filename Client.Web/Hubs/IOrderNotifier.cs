using Shared.Contracts.Enums;

namespace Client.Web.Hubs;

public interface IOrderNotifier
{
    Task OrderStatusUpdatedAsync(
        Guid customerId,
        Guid orderId,
        OrderStatus newStatus,
        DateTime? estimatedTime,
        CancellationToken ct = default);
}
