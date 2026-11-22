using System.Text.Json;
using Shared.Contracts.Messages;
using Shared.Contracts.Serialization;
using Shared.Messaging;

namespace Shared.Persistence.Outbox;

public class OutboxMessage
{
    private static IReadOnlyDictionary<string, System.Type> MessageTypes { get; } =
        RabbitMqTopology.Bindings.ToDictionary(b => b.MessageType.Name, b => b.MessageType);

    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public Guid? CorrelationId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public static OutboxMessage From(MessageBase message)
    {
        System.Type messageType = message.GetType();
        if (!MessageTypes.ContainsKey(messageType.Name))
        {
            throw new ArgumentException(
                $"{messageType.Name} has no queue. Add it to RabbitMqTopology.Bindings before sending it.",
                nameof(message));
        }

        return new OutboxMessage
        {
            Id = message.MessageId,
            Type = messageType.Name,
            CorrelationId = message.CorrelationId,
            Payload = JsonSerializer.Serialize(message, messageType, JsonOptions.Default),
            CreatedAt = message.CreatedAt
        };
    }

    public MessageBase ToMessage()
    {
        if (!MessageTypes.TryGetValue(Type, out System.Type? messageType))
        {
            throw new InvalidOperationException($"Outbox row {Id} has an unknown message type {Type}.");
        }

        return JsonSerializer.Deserialize(Payload, messageType, JsonOptions.Default) as MessageBase
            ?? throw new InvalidOperationException($"Outbox row {Id} has an empty payload.");
    }
}
