using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Contracts.Messages;
using Shared.Contracts.Serialization;

namespace Shared.Messaging;

public abstract class RabbitMqConsumerBase<TMessage>(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger logger) : BackgroundService where TMessage : MessageBase
{
    private const ushort PrefetchCount = 10;

    public static string QueueName { get; } = FindQueue();

    public abstract Task HandleAsync(TMessage message, IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IChannel channel = await connection.CreateChannelAsync(stoppingToken);
        try
        {
            await channel.BasicQosAsync(0, PrefetchCount, false, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery, stoppingToken);
            await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);

            logger.LogInformation("Consuming {MessageType} from {Queue}", typeof(TMessage).Name, QueueName);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Stopped consuming {MessageType} from {Queue}", typeof(TMessage).Name, QueueName);
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        string? messageId = delivery.BasicProperties.MessageId;
        try
        {
            TMessage message = JsonSerializer.Deserialize<TMessage>(delivery.Body.Span, JsonOptions.Default)
                ?? throw new JsonException("The message body is empty.");

            await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
            {
                await HandleAsync(message, scope.ServiceProvider, stoppingToken);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
            logger.LogInformation("Handled {MessageType} {MessageId} from {Queue}", typeof(TMessage).Name, messageId, QueueName);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to handle {MessageType} {MessageId} from {Queue}", typeof(TMessage).Name, messageId, QueueName);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        }
    }

    private static string FindQueue() =>
        RabbitMqTopology.Bindings.FirstOrDefault(b => b.MessageType == typeof(TMessage))?.Queue
        ?? throw new InvalidOperationException($"{typeof(TMessage).Name} has no queue in RabbitMqTopology.Bindings.");
}
