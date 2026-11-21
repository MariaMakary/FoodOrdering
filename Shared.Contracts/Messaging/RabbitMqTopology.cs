using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Shared.Contracts.Messages;
using Shared.Contracts.Options;

namespace Shared.Contracts.Messaging;

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

        foreach (var binding in Bindings)
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

    public static async Task DeclareAsync(RabbitMqOptions options, CancellationToken ct = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.Username,
            Password = options.Password,
            VirtualHost = options.VirtualHost
        };

        var attempt = 0;
        while (true)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync("topology-setup", ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await DeclareAsync(channel, options.ExchangeName, ct);
                return;
            }
            catch (BrokerUnreachableException) when (attempt < options.RetryCount)
            {
                attempt++;
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }
    }

    private static Dictionary<string, object?> QueueArguments() => new();
}
