using Admin.Web.Data;
using Admin.Web.Models.Entities;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;

namespace FoodOrdering.IntegrationTests.Admin;

public static class AdminOrderSeed
{
    public static readonly DateTime PlacedAt = new(2026, 11, 24, 12, 30, 0, DateTimeKind.Utc);

    public static async Task<Guid> AddMenuItemAsync(AdminDbContext context)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Burgers" };
        var burger = new MenuItem { Id = Guid.NewGuid(), CategoryId = category.Id, Name = "Chicken Burger", Price = 9.50m };
        context.Categories.Add(category);
        context.MenuItems.Add(burger);
        await context.SaveChangesAsync();
        return burger.Id;
    }

    public static async Task<Order> AddOrderAsync(
        AdminDbContext context,
        OrderStatus status = OrderStatus.Placed,
        DateTime? statusChangedAt = null,
        DateTime? estimatedDeliveryTime = null,
        string? statusNote = null,
        Guid? orderId = null)
    {
        var order = new Order
        {
            Id = orderId ?? Guid.NewGuid(),
            OrderNumber = $"FO-{Random.Shared.Next(1000, 999999)}",
            CustomerId = Guid.NewGuid(),
            CustomerName = "Sara Haddad",
            CustomerPhone = "+961 70 123 456",
            DeliveryType = DeliveryType.Delivery,
            Address = "Hamra Street, Beirut",
            PaymentMethod = PaymentMethod.Online,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = status,
            Total = 27.00m,
            EstimatedDeliveryTime = estimatedDeliveryTime,
            PlacedAt = PlacedAt
        };
        order.StatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), Status = OrderStatus.Placed, ChangedAt = PlacedAt });
        if (status != OrderStatus.Placed)
        {
            order.StatusHistory.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                Status = status,
                ChangedAt = statusChangedAt ?? PlacedAt.AddMinutes(2),
                Note = statusNote
            });
        }

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    public static OrderPlaced OrderPlacedFor(Guid orderId, Guid menuItemId) => new()
    {
        CorrelationId = orderId,
        OrderId = orderId,
        OrderNumber = "FO-1042",
        CustomerId = Guid.NewGuid(),
        CustomerName = "Sara Haddad",
        CustomerPhone = "+961 70 123 456",
        DeliveryType = DeliveryType.Delivery,
        Address = "Hamra Street, Beirut",
        PaymentMethod = PaymentMethod.Online,
        Items = [new OrderItemDto { MenuItemId = menuItemId, Name = "Chicken Burger", UnitPrice = 9.50m, Quantity = 1 }],
        Total = 9.50m,
        PlacedAt = PlacedAt
    };
}
