using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Shared.Messaging;

public sealed class RabbitMqTopologyInitializer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqTopologyInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        RabbitMqOptions settings = options.Value;
        logger.LogInformation("RabbitMQ: {Host}:{Port}, vhost {VirtualHost}, exchange {Exchange}, user {Username}",
            settings.Host, settings.Port, settings.VirtualHost, settings.ExchangeName, settings.Username);

        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, settings.ExchangeName, cancellationToken);

        logger.LogInformation("RabbitMQ topology declared: exchange {Exchange} with {QueueCount} queues",
            settings.ExchangeName, RabbitMqTopology.Bindings.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
