using Admin.Web.Services;
using Shared.Contracts.Messages;
using Shared.Messaging;

namespace Admin.Web.Messaging.Consumers;

public class OrderCancelledConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderCancelledConsumer> logger) : RabbitMqConsumerBase<OrderCancelled>(connection, scopeFactory, logger)
{
    public override Task HandleAsync(OrderCancelled message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IOrderCancellationService>().ApplyCancelAsync(message, ct);
}
