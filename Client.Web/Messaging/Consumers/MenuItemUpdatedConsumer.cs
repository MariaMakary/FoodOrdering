using Client.Web.Services;
using Shared.Contracts.Messages;
using Shared.Messaging;

namespace Client.Web.Messaging.Consumers;

public class MenuItemUpdatedConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<MenuItemUpdatedConsumer> logger) : RabbitMqConsumerBase<MenuItemUpdated>(connection, scopeFactory, logger)
{
    public override Task HandleAsync(MenuItemUpdated message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IMenuSyncService>().ApplyMenuItemUpdatedAsync(message, ct);
}
