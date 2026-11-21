using System.Text.Json;
using System.Text.Json.Nodes;
using FoodOrdering.UnitTests.Helpers;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Contracts.Serialization;
using Shouldly;

namespace FoodOrdering.UnitTests.Contracts;

public class JsonOptionsTests
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
    public void MessageExamples_ShouldCoverAllSixMessages()
    {
        MessageExamples.All.Select(e => e.MessageType).Distinct().ShouldBe(
            [
                nameof(OrderPlaced), nameof(PaymentCompleted), nameof(OrderCancelled),
                nameof(DishRated), nameof(OrderStatusChanged), nameof(MenuItemUpdated)
            ],
            ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(DocsExamples))]
    public void RoundTrip_ShouldProduceTheSameJson_ForEveryDocsExample(string messageType, string json)
    {
        Type type = typeof(MessageBase).Assembly.GetType($"{typeof(MessageBase).Namespace}.{messageType}")!;

        object? message = JsonSerializer.Deserialize(json, type, JsonOptions.Default);
        string output = JsonSerializer.Serialize(message, type, JsonOptions.Default);

        JsonNode.DeepEquals(JsonNode.Parse(output), JsonNode.Parse(json))
            .ShouldBeTrue($"{messageType} should serialize back to the example in docs/messages.md, but produced:\n{output}");
    }

    [Fact]
    public void Deserialize_ShouldReadEveryField_WhenGivenTheOrderPlacedExample()
    {
        OrderPlaced message = JsonSerializer.Deserialize<OrderPlaced>(MessageExamples.First(nameof(OrderPlaced)), JsonOptions.Default)!;

        message.OrderNumber.ShouldBe("FO-1042");
        message.CorrelationId.ShouldBe(message.OrderId);
        message.DeliveryType.ShouldBe(DeliveryType.Delivery);
        message.PaymentMethod.ShouldBe(PaymentMethod.Online);
        message.Items.Count.ShouldBe(2);
        message.Items[0].Extras.Count.ShouldBe(2);
        message.Items[0].Extras[1].ShouldBe(new ExtraDto { Name = "Bacon", Price = 1.50m });
        message.Total.ShouldBe(27.00m);
    }

    [Fact]
    public void Serialize_ShouldWriteEnumsAsNames()
    {
        var message = new OrderStatusChanged
        {
            OrderId = Guid.NewGuid(),
            NewStatus = OrderStatus.OutForDelivery,
            ChangedAt = DateTime.UtcNow
        };

        JsonNode json = JsonNode.Parse(JsonSerializer.Serialize(message, JsonOptions.Default))!;

        json["newStatus"]!.GetValue<string>().ShouldBe("OutForDelivery");
    }

    [Fact]
    public void Deserialize_ShouldThrow_WhenAnEnumIsSentAsANumber()
    {
        string json = MessageExamples.First(nameof(OrderStatusChanged)).Replace("\"Accepted\"", "1");

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<OrderStatusChanged>(json, JsonOptions.Default));
    }

    [Fact]
    public void Serialize_ShouldEndUtcDatesWithZ()
    {
        var message = new OrderCancelled
        {
            OrderId = Guid.NewGuid(),
            CancelledAt = new DateTime(2026, 11, 18, 12, 32, 5, DateTimeKind.Utc)
        };

        JsonNode json = JsonNode.Parse(JsonSerializer.Serialize(message, JsonOptions.Default))!;

        json["cancelledAt"]!.GetValue<string>().ShouldBe("2026-11-18T12:32:05Z");
    }

    [Fact]
    public void Deserialize_ShouldKeepUtcKind_WhenTheDateEndsWithZ()
    {
        PaymentCompleted message = JsonSerializer.Deserialize<PaymentCompleted>(MessageExamples.First(nameof(PaymentCompleted)), JsonOptions.Default)!;

        message.PaidAt.Kind.ShouldBe(DateTimeKind.Utc);
        message.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        message.PaidAt.ShouldBe(new DateTime(2026, 11, 18, 12, 30, 18, DateTimeKind.Utc));
    }

    [Fact]
    public void Deserialize_ShouldThrow_WhenARequiredFieldIsMissing()
    {
        JsonObject json = JsonNode.Parse(MessageExamples.First(nameof(DishRated)))!.AsObject();
        json.Remove("stars");

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<DishRated>(json.ToJsonString(), JsonOptions.Default));
    }

    [Fact]
    public void Serialize_ShouldWriteNull_WhenAnOptionalFieldIsEmpty()
    {
        var message = new OrderCancelled { OrderId = Guid.NewGuid(), CancelledAt = DateTime.UtcNow };

        JsonObject json = JsonNode.Parse(JsonSerializer.Serialize(message, JsonOptions.Default))!.AsObject();

        json.ContainsKey("reason").ShouldBeTrue();
        json["reason"].ShouldBeNull();
    }
}
