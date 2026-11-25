using Client.Web.Services;
using Shared.Contracts.Messages;
using Shared.Messaging;

namespace Client.Web.Messaging.Consumers;

public class OrderStatusChangedConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderStatusChangedConsumer> logger) : RabbitMqConsumerBase<OrderStatusChanged>(connection, scopeFactory, logger)
{
    public override Task HandleAsync(OrderStatusChanged message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IOrderStatusService>().ApplyStatusChangeAsync(message, ct);
}
