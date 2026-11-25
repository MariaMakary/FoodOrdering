using Client.Web.Models.Entities;
using Client.Web.Repositories;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;

namespace Client.Web.Services;

public class OrderStatusService(IOrderStatusRepository orders, ILogger<OrderStatusService> logger) : IOrderStatusService
{
    public const string CancelRefusedNote = "The restaurant had already accepted your order, so it could not be cancelled.";

    public async Task ApplyStatusChangeAsync(OrderStatusChanged message, CancellationToken ct = default)
    {
        Order? order = await orders.GetOrderAsync(message.OrderId, ct);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} does not exist, ignoring the OrderStatusChanged to {NewStatus}",
                message.OrderId, message.NewStatus);
            return;
        }

        bool overridesCustomerCancel = order.Status == OrderStatus.Cancelled && message.NewStatus != OrderStatus.Cancelled;
        if (!overridesCustomerCancel && message.ChangedAt <= order.LastStatusChangedAt)
        {
            logger.LogInformation(
                "Ignoring OrderStatusChanged to {NewStatus} for order {OrderId}: changed at {ChangedAt}, last applied change at {LastStatusChangedAt}",
                message.NewStatus, message.OrderId, message.ChangedAt, order.LastStatusChangedAt);
            return;
        }

        OrderStatus previousStatus = order.Status;
        order.Status = message.NewStatus;
        order.EstimatedDeliveryTime = message.EstimatedDeliveryTime ?? order.EstimatedDeliveryTime;
        if (message.NewStatus == OrderStatus.Rejected)
        {
            order.RejectionReason = message.Reason;
        }

        order.LastStatusChangedAt = message.ChangedAt;

        orders.AddHistory(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Status = message.NewStatus,
            ChangedAt = message.ChangedAt,
            Note = message.Reason ?? (overridesCustomerCancel ? CancelRefusedNote : null)
        });

        await orders.SaveChangesAsync(ct);

        logger.LogInformation("Order {OrderId} changed from {PreviousStatus} to {NewStatus}",
            order.Id, previousStatus, message.NewStatus);
    }
}
