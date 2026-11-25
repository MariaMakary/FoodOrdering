using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Contracts.Messages;
using Shared.Contracts.Serialization;
using Shared.Messaging;

namespace FoodOrdering.IntegrationTests.Infrastructure;

public static class ConsumerDeliveries
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    public static BasicDeliverEventArgs Create(MessageBase message, ulong deliveryTag)
    {
        Type messageType = message.GetType();
        var properties = new BasicProperties
        {
            MessageId = message.MessageId.ToString(),
            CorrelationId = message.CorrelationId?.ToString(),
            Type = messageType.Name,
            ContentType = "application/json"
        };

        return new BasicDeliverEventArgs(
            "test-consumer",
            deliveryTag,
            redelivered: false,
            RabbitMqFixture.ExchangeName,
            RabbitMqTopology.RoutingKeyFor(messageType),
            properties,
            JsonSerializer.SerializeToUtf8Bytes(message, messageType, JsonOptions.Default));
    }

    public static async Task WaitUntilASessionWaitsForALockAsync(DbContext monitor)
    {
        DateTime deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            int waiting = await monitor.Database
                .SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
                .SingleAsync();
            if (waiting > 0)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("No session started waiting for a lock.");
            }

            await Task.Delay(50);
        }
    }
}
