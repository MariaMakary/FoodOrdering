using Admin.Web.Services;
using Shared.Contracts.Messages;
using Shared.Messaging;

namespace Admin.Web.Messaging.Consumers;

public class DishRatedConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<DishRatedConsumer> logger) : RabbitMqConsumerBase<DishRated>(connection, scopeFactory, logger)
{
    public override Task HandleAsync(DishRated message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IRatingService>().SaveRatingAsync(message, ct);
}
