using System.Text.Json;
using System.Text.Json.Nodes;
using FoodOrdering.UnitTests.Helpers;
using Shared.Contracts.Messages;
using Shared.Contracts.Serialization;
using Shared.Persistence.Outbox;
using Shouldly;

namespace FoodOrdering.UnitTests.Persistence;

public class OutboxMessageTests
{
    public static TheoryData<string, string> DocsExamples()
    {
        var data = new TheoryData<string, string>();
        foreach ((string messageType, string json) in MessageExamples.All)
        {
            data.Add(messageType, json);
        }
        return data;
    }

    [Fact]
    public void From_ShouldCopyTheMessageIdTypeCorrelationIdAndCreatedAt()
    {
        OrderPlaced message = JsonSerializer.Deserialize<OrderPlaced>(MessageExamples.First(nameof(OrderPlaced)), JsonOptions.Default)!;

        OutboxMessage row = OutboxMessage.From(message);

        row.Id.ShouldBe(message.MessageId);
        row.Type.ShouldBe(nameof(OrderPlaced));
        row.CorrelationId.ShouldBe(message.CorrelationId);
        row.CreatedAt.ShouldBe(message.CreatedAt);
        row.SentAt.ShouldBeNull();
        row.Attempts.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(DocsExamples))]
    public void ToMessage_ShouldReturnTheSameMessage_ForEveryDocsExample(string messageType, string json)
    {
        Type type = typeof(MessageBase).Assembly.GetType($"{typeof(MessageBase).Namespace}.{messageType}")!;
        var message = (MessageBase)JsonSerializer.Deserialize(json, type, JsonOptions.Default)!;

        MessageBase result = OutboxMessage.From(message).ToMessage();

        result.ShouldBeOfType(type);
        string output = JsonSerializer.Serialize(result, type, JsonOptions.Default);
        JsonNode.DeepEquals(JsonNode.Parse(output), JsonNode.Parse(json)).ShouldBeTrue(output);
    }

    [Fact]
    public void From_ShouldThrow_WhenTheMessageHasNoQueue()
    {
        Should.Throw<ArgumentException>(() => OutboxMessage.From(new MessageWithoutQueue()))
            .Message.ShouldContain(nameof(MessageWithoutQueue));
    }

    [Fact]
    public void ToMessage_ShouldThrow_WhenTheTypeIsUnknown()
    {
        var row = new OutboxMessage { Id = Guid.NewGuid(), Type = "Renamed", Payload = "{}" };

        Should.Throw<InvalidOperationException>(() => row.ToMessage()).Message.ShouldContain("Renamed");
    }

    private sealed record MessageWithoutQueue : MessageBase;
}
