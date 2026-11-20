using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using FoodOrdering.Tests.Helpers;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Contracts.Serialization;

namespace FoodOrdering.Tests.Contracts;

public class JsonOptionsTests
{
    public static TheoryData<string, string> DocsExamples()
    {
        var data = new TheoryData<string, string>();
        foreach (var (messageType, json) in MessageExamples.All)
        {
            data.Add(messageType, json);
        }
        return data;
    }

    [Fact]
    public void DocsExamples_Loaded_CoverAllSixMessages()
    {
        MessageExamples.All.Select(e => e.MessageType).Distinct().Should().BeEquivalentTo(
            nameof(OrderPlaced), nameof(PaymentCompleted), nameof(OrderCancelled),
            nameof(DishRated), nameof(OrderStatusChanged), nameof(MenuItemUpdated));
    }

    [Theory]
    [MemberData(nameof(DocsExamples))]
    public void RoundTrip_DocsExample_ProducesSameJson(string messageType, string json)
    {
        var type = typeof(MessageBase).Assembly.GetType($"{typeof(MessageBase).Namespace}.{messageType}")!;

        var message = JsonSerializer.Deserialize(json, type, JsonOptions.Default);
        var output = JsonSerializer.Serialize(message, type, JsonOptions.Default);

        JsonNode.DeepEquals(JsonNode.Parse(output), JsonNode.Parse(json))
            .Should().BeTrue($"{messageType} should serialize back to the example in docs/messages.md, but produced:\n{output}");
    }

    [Fact]
    public void Deserialize_OrderPlacedDocsExample_ReadsEveryField()
    {
        var message = JsonSerializer.Deserialize<OrderPlaced>(MessageExamples.First(nameof(OrderPlaced)), JsonOptions.Default)!;

        message.OrderNumber.Should().Be("FO-1042");
        message.CorrelationId.Should().Be(message.OrderId);
        message.DeliveryType.Should().Be(DeliveryType.Delivery);
        message.PaymentMethod.Should().Be(PaymentMethod.Online);
        message.Items.Should().HaveCount(2);
        message.Items[0].Extras.Should().HaveCount(2);
        message.Items[0].Extras[1].Should().Be(new ExtraDto { Name = "Bacon", Price = 1.50m });
        message.Total.Should().Be(27.00m);
    }

    [Fact]
    public void Serialize_Enums_WritesNamesNotNumbers()
    {
        var message = new OrderStatusChanged
        {
            OrderId = Guid.NewGuid(),
            NewStatus = OrderStatus.OutForDelivery,
            ChangedAt = DateTime.UtcNow
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(message, JsonOptions.Default))!;

        json["newStatus"]!.GetValue<string>().Should().Be("OutForDelivery");
    }

    [Fact]
    public void Deserialize_EnumAsNumber_Throws()
    {
        var json = MessageExamples.First(nameof(OrderStatusChanged)).Replace("\"Accepted\"", "1");

        var act = () => JsonSerializer.Deserialize<OrderStatusChanged>(json, JsonOptions.Default);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Serialize_UtcDate_EndsWithZ()
    {
        var message = new OrderCancelled
        {
            OrderId = Guid.NewGuid(),
            CancelledAt = new DateTime(2026, 11, 18, 12, 32, 5, DateTimeKind.Utc)
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(message, JsonOptions.Default))!;

        json["cancelledAt"]!.GetValue<string>().Should().Be("2026-11-18T12:32:05Z");
    }

    [Fact]
    public void Deserialize_DateWithZ_KeepsUtcKind()
    {
        var message = JsonSerializer.Deserialize<PaymentCompleted>(MessageExamples.First(nameof(PaymentCompleted)), JsonOptions.Default)!;

        message.PaidAt.Kind.Should().Be(DateTimeKind.Utc);
        message.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        message.PaidAt.Should().Be(new DateTime(2026, 11, 18, 12, 30, 18, DateTimeKind.Utc));
    }

    [Fact]
    public void Deserialize_RequiredFieldMissing_Throws()
    {
        var json = JsonNode.Parse(MessageExamples.First(nameof(DishRated)))!.AsObject();
        json.Remove("stars");

        var act = () => JsonSerializer.Deserialize<DishRated>(json.ToJsonString(), JsonOptions.Default);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Serialize_NullOptionalField_WritesNull()
    {
        var message = new OrderCancelled { OrderId = Guid.NewGuid(), CancelledAt = DateTime.UtcNow };

        var json = JsonNode.Parse(JsonSerializer.Serialize(message, JsonOptions.Default))!.AsObject();

        json.ContainsKey("reason").Should().BeTrue();
        json["reason"].Should().BeNull();
    }
}
