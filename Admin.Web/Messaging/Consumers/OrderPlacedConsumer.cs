using Admin.Web.Services;
using Shared.Contracts.Messages;
using Shared.Messaging;

namespace Admin.Web.Messaging.Consumers;

public class OrderPlacedConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderPlacedConsumer> logger) : RabbitMqConsumerBase<OrderPlaced>(connection, scopeFactory, logger)
{
    public override Task HandleAsync(OrderPlaced message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IOrderService>().CreateFromMessageAsync(message, ct);
}
