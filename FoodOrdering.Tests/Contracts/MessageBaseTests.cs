using FluentAssertions;
using Shared.Contracts.Messages;

namespace FoodOrdering.Tests.Contracts;

public class MessageBaseTests
{
    [Fact]
    public void New_TwoMessages_GetDifferentMessageIds()
    {
        var first = new OrderCancelled { OrderId = Guid.NewGuid(), CancelledAt = DateTime.UtcNow };
        var second = first with { };

        var third = new OrderCancelled { OrderId = first.OrderId, CancelledAt = first.CancelledAt };

        first.MessageId.Should().NotBe(Guid.Empty);
        third.MessageId.Should().NotBe(first.MessageId);
        second.MessageId.Should().Be(first.MessageId);
    }

    [Fact]
    public void New_Message_HasUtcCreatedAtAndNoCorrelationId()
    {
        var message = new OrderCancelled { OrderId = Guid.NewGuid(), CancelledAt = DateTime.UtcNow };

        message.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        message.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        message.CorrelationId.Should().BeNull();
    }
}
