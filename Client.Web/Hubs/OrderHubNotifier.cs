using Microsoft.AspNetCore.SignalR;
using Shared.Contracts.Enums;

namespace Client.Web.Hubs;

public class OrderHubNotifier(IHubContext<OrderHub> hub) : IOrderNotifier
{
    public Task OrderStatusUpdatedAsync(
        Guid customerId,
        Guid orderId,
        OrderStatus newStatus,
        DateTime? estimatedTime,
        CancellationToken ct = default) =>
        hub.Clients.User(customerId.ToString())
            .SendAsync(OrderHub.OrderStatusUpdatedEvent, orderId, newStatus, estimatedTime, ct);
}
