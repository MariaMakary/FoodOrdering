using Shared.Contracts.Messages;
using Shouldly;

namespace FoodOrdering.UnitTests.Contracts;

public class MessageBaseTests
{
    [Fact]
    public void Create_ShouldGiveEachNewMessageItsOwnMessageId()
    {
        var first = new OrderCancelled { OrderId = Guid.NewGuid(), CancelledAt = DateTime.UtcNow };
        var second = new OrderCancelled { OrderId = first.OrderId, CancelledAt = first.CancelledAt };

        first.MessageId.ShouldNotBe(Guid.Empty);
        second.MessageId.ShouldNotBe(first.MessageId);
    }

    [Fact]
    public void With_ShouldKeepTheMessageId_WhenTheSameMessageIsCopiedForARetry()
    {
        var original = new OrderCancelled { OrderId = Guid.NewGuid(), CancelledAt = DateTime.UtcNow };

        OrderCancelled retry = original with { };

        retry.MessageId.ShouldBe(original.MessageId);
    }

    [Fact]
    public void Create_ShouldSetAUtcCreatedAtAndNoCorrelationId()
    {
        var message = new OrderCancelled { OrderId = Guid.NewGuid(), CancelledAt = DateTime.UtcNow };

        message.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        message.CreatedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        message.CorrelationId.ShouldBeNull();
    }
}
