using Admin.Web.Models.Entities;
using Admin.Web.Repositories;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;

namespace Admin.Web.Services;

public class OrderService(IOrderRepository orders, ILogger<OrderService> logger) : IOrderService
{
    public async Task CreateFromMessageAsync(OrderPlaced message, CancellationToken ct = default)
    {
        if (await orders.ExistsAsync(message.OrderId, ct))
        {
            logger.LogWarning("Order {OrderId} ({OrderNumber}) already exists, ignoring the OrderPlaced message",
                message.OrderId, message.OrderNumber);
            return;
        }

        orders.Add(ToOrder(message));
        await orders.SaveChangesAsync(ct);

        logger.LogInformation("Order {OrderId} ({OrderNumber}) received with {ItemCount} items",
            message.OrderId, message.OrderNumber, message.Items.Count);
    }

    private static Order ToOrder(OrderPlaced message) => new()
    {
        Id = message.OrderId,
        OrderNumber = message.OrderNumber,
        CustomerId = message.CustomerId,
        CustomerName = message.CustomerName,
        CustomerPhone = message.CustomerPhone,
        Address = message.Address,
        Notes = message.Notes,
        DeliveryType = message.DeliveryType,
        PaymentMethod = message.PaymentMethod,
        PaymentStatus = message.PaymentMethod == PaymentMethod.Cash ? PaymentStatus.CashOnDelivery : PaymentStatus.Unpaid,
        Status = OrderStatus.Placed,
        Total = message.Total,
        PlacedAt = message.PlacedAt,
        Items = message.Items.Select(ToOrderItem).ToList(),
        StatusHistory =
        [
            new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                Status = OrderStatus.Placed,
                ChangedAt = message.PlacedAt
            }
        ]
    };

    private static OrderItem ToOrderItem(OrderItemDto item) => new()
    {
        Id = Guid.NewGuid(),
        MenuItemId = item.MenuItemId,
        Name = item.Name,
        UnitPrice = item.UnitPrice,
        Quantity = item.Quantity,
        Extras = item.Extras.Select(extra => new OrderItemExtra
        {
            Id = Guid.NewGuid(),
            Name = extra.Name,
            Price = extra.Price
        }).ToList()
    };
}
