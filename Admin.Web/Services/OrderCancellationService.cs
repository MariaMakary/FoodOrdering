using Admin.Web.Models.Entities;
using Admin.Web.Repositories;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;

namespace Admin.Web.Services;

public class OrderCancellationService(
    IOrderRepository orders,
    IOutbox outbox,
    ILogger<OrderCancellationService> logger) : IOrderCancellationService
{
    public async Task ApplyCancelAsync(OrderCancelled message, CancellationToken ct = default)
    {
        Order order = await orders.GetOrderAsync(message.OrderId, ct)
            ?? throw new OrderNotReceivedException(message.OrderId, nameof(OrderCancelled));

        switch (order.Status)
        {
            case OrderStatus.Cancelled:
                logger.LogInformation("Order {OrderId} is already Cancelled, ignoring the OrderCancelled message", order.Id);
                return;
            case OrderStatus.Placed:
                await CancelAsync(order, message, ct);
                return;
            default:
                await RefuseCancelAsync(order, message, ct);
                return;
        }
    }

    private async Task CancelAsync(Order order, OrderCancelled message, CancellationToken ct)
    {
        order.Status = OrderStatus.Cancelled;
        orders.AddHistory(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Status = OrderStatus.Cancelled,
            ChangedAt = message.CancelledAt,
            ChangedByUserId = null,
            Note = message.Reason
        });

        await orders.SaveChangesAsync(ct);

        logger.LogInformation("Order {OrderId} cancelled by the customer at {CancelledAt}", order.Id, message.CancelledAt);
    }

    private async Task RefuseCancelAsync(Order order, OrderCancelled message, CancellationToken ct)
    {
        OrderStatusHistory? currentStatusChange = await orders.GetLatestStatusChangeAsync(order.Id, order.Status, ct);

        outbox.Add(new OrderStatusChanged
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            NewStatus = order.Status,
            EstimatedDeliveryTime = order.EstimatedDeliveryTime,
            Reason = order.Status == OrderStatus.Rejected ? currentStatusChange?.Note : null,
            ChangedAt = currentStatusChange?.ChangedAt ?? DateTime.UtcNow
        });

        await orders.SaveChangesAsync(ct);

        logger.LogWarning(
            "Ignoring the cancel of order {OrderId} sent at {CancelledAt}: the order is already {Status}. Sent its current status to the Client again at {RefusedAt}",
            order.Id, message.CancelledAt, order.Status, DateTime.UtcNow);
    }
}
