namespace Shared.Contracts.Messaging;

public static class RoutingKeys
{
    public const string OrderPlaced = "order.placed";
    public const string PaymentCompleted = "payment.completed";
    public const string OrderCancelled = "order.cancelled";
    public const string DishRated = "dish.rated";
    public const string OrderStatusChanged = "order.status.changed";
    public const string MenuItemUpdated = "menu.item.updated";
}
