using RabbitMQ.Client;
using Shared.Contracts.Messages;
using Shared.Contracts.Messaging;

namespace Shared.Messaging;

public sealed record QueueBinding(Type MessageType, string RoutingKey, string Queue);

public static class RabbitMqTopology
{
    public static IReadOnlyList<QueueBinding> Bindings { get; } =
    [
        new(typeof(OrderPlaced), RoutingKeys.OrderPlaced, QueueNames.AdminOrderPlaced),
        new(typeof(PaymentCompleted), RoutingKeys.PaymentCompleted, QueueNames.AdminPaymentCompleted),
        new(typeof(OrderCancelled), RoutingKeys.OrderCancelled, QueueNames.AdminOrderCancelled),
        new(typeof(DishRated), RoutingKeys.DishRated, QueueNames.AdminDishRated),
        new(typeof(OrderStatusChanged), RoutingKeys.OrderStatusChanged, QueueNames.ClientOrderStatusChanged),
        new(typeof(MenuItemUpdated), RoutingKeys.MenuItemUpdated, QueueNames.ClientMenuItemUpdated),
    ];

    public static string RoutingKeyFor(Type messageType) =>
        Bindings.FirstOrDefault(b => b.MessageType == messageType)?.RoutingKey
        ?? throw new ArgumentException($"No routing key is defined for {messageType.Name}.", nameof(messageType));

    public static async Task DeclareAsync(IChannel channel, string exchangeName, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        foreach (QueueBinding binding in Bindings)
        {
            await channel.QueueDeclareAsync(
                queue: binding.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: QueueArguments(),
                cancellationToken: ct);

            await channel.QueueBindAsync(
                queue: binding.Queue,
                exchange: exchangeName,
                routingKey: binding.RoutingKey,
                cancellationToken: ct);
        }
    }

    private static Dictionary<string, object?> QueueArguments() => new();
}
