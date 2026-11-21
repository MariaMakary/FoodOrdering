namespace Shared.Contracts.Messaging;

public static class QueueNames
{
    public const string AdminOrderPlaced = "admin.order-placed";
    public const string AdminPaymentCompleted = "admin.payment-completed";
    public const string AdminOrderCancelled = "admin.order-cancelled";
    public const string AdminDishRated = "admin.dish-rated";
    public const string ClientOrderStatusChanged = "client.order-status-changed";
    public const string ClientMenuItemUpdated = "client.menu-item-updated";
}
