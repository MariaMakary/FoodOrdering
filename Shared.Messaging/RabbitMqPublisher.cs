using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shared.Contracts.Messages;
using Shared.Contracts.Serialization;

namespace Shared.Messaging;

public sealed class RabbitMqPublisher(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqPublisher> logger) : IMessagePublisher, IAsyncDisposable
{
    private static readonly CreateChannelOptions ConfirmedChannel =
        new(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(MessageBase message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        Type messageType = message.GetType();
        string routingKey = RabbitMqTopology.RoutingKeyFor(messageType);
        string exchange = options.Value.ExchangeName;
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, messageType, JsonOptions.Default);
        BasicProperties properties = CreateProperties(message);

        await _lock.WaitAsync(ct);
        try
        {
            IChannel channel = await GetChannelAsync(ct);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, body, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to publish {MessageType} {MessageId} to {Exchange} with routing key {RoutingKey}",
                messageType.Name, message.MessageId, exchange, routingKey);
            throw;
        }
        finally
        {
            _lock.Release();
        }

        logger.LogInformation("Published {MessageType} {MessageId} to {Exchange} with routing key {RoutingKey}, correlation {CorrelationId}",
            messageType.Name, message.MessageId, exchange, routingKey, message.CorrelationId);
    }

    private static BasicProperties CreateProperties(MessageBase message) => new()
    {
        Persistent = true,
        MessageId = message.MessageId.ToString(),
        CorrelationId = message.CorrelationId?.ToString(),
        Type = message.GetType().Name,
        ContentType = "application/json",
        Timestamp = new AmqpTimestamp(new DateTimeOffset(DateTime.SpecifyKind(message.CreatedAt, DateTimeKind.Utc)).ToUnixTimeSeconds())
    };

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _channel = await connection.CreateChannelAsync(ConfirmedChannel, ct);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _lock.Dispose();
    }
}
