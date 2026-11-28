using Admin.Web.Models.Entities;
using Admin.Web.Repositories;
using Admin.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;
using Shouldly;

namespace FoodOrdering.UnitTests.Admin.Services;

public class OrderCancellationServiceTests
{
    private static readonly DateTime PlacedAt = new(2026, 11, 24, 12, 30, 0, DateTimeKind.Utc);

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly OrderCancellationService _service;

    public OrderCancellationServiceTests()
    {
        _service = new OrderCancellationService(_orders, _outbox, NullLogger<OrderCancellationService>.Instance);
    }

    [Fact]
    public async Task ApplyCancelAsync_ShouldCancelTheOrderAddAHistoryRowAndSave_WhenTheOrderIsPlaced()
    {
        Order order = GivenOrder(OrderStatus.Placed);
        OrderCancelled message = Message(order.Id);

        await _service.ApplyCancelAsync(message);

        order.Status.ShouldBe(OrderStatus.Cancelled);
        OrderStatusHistory history = AddedHistory();
        history.OrderId.ShouldBe(order.Id);
        history.Status.ShouldBe(OrderStatus.Cancelled);
        history.ChangedAt.ShouldBe(message.CancelledAt);
        history.ChangedByUserId.ShouldBeNull();
        history.Note.ShouldBe("Ordered by mistake");
        _outbox.DidNotReceive().Add(Arg.Any<MessageBase>());
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyCancelAsync_ShouldDoNothing_WhenTheOrderIsAlreadyCancelled()
    {
        Order order = GivenOrder(OrderStatus.Cancelled);

        await _service.ApplyCancelAsync(Message(order.Id));

        order.Status.ShouldBe(OrderStatus.Cancelled);
        _orders.DidNotReceive().AddHistory(Arg.Any<OrderStatusHistory>());
        _outbox.DidNotReceive().Add(Arg.Any<MessageBase>());
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyCancelAsync_ShouldIgnoreTheCancelAndSendTheCurrentStatusAgain_WhenTheOrderIsAlreadyAccepted()
    {
        Order order = GivenOrder(OrderStatus.Accepted);
        order.EstimatedDeliveryTime = PlacedAt.AddMinutes(45);
        GivenLatestChange(order, OrderStatus.Accepted, PlacedAt.AddMinutes(2));

        await _service.ApplyCancelAsync(Message(order.Id));

        order.Status.ShouldBe(OrderStatus.Accepted);
        _orders.DidNotReceive().AddHistory(Arg.Any<OrderStatusHistory>());
        OrderStatusChanged sent = SentMessage();
        sent.OrderId.ShouldBe(order.Id);
        sent.CorrelationId.ShouldBe(order.Id);
        sent.NewStatus.ShouldBe(OrderStatus.Accepted);
        sent.EstimatedDeliveryTime.ShouldBe(PlacedAt.AddMinutes(45));
        sent.Reason.ShouldBeNull();
        sent.ChangedAt.ShouldBe(PlacedAt.AddMinutes(2));
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OutForDelivery)]
    [InlineData(OrderStatus.Delivered)]
    public async Task ApplyCancelAsync_ShouldSendTheCurrentStatusAgain_WhenTheOrderIsPastAccepted(OrderStatus status)
    {
        Order order = GivenOrder(status);
        GivenLatestChange(order, status, PlacedAt.AddMinutes(10));

        await _service.ApplyCancelAsync(Message(order.Id));

        order.Status.ShouldBe(status);
        OrderStatusChanged sent = SentMessage();
        sent.NewStatus.ShouldBe(status);
        sent.ChangedAt.ShouldBe(PlacedAt.AddMinutes(10));
    }

    [Fact]
    public async Task ApplyCancelAsync_ShouldSendTheRejectionReasonAgain_WhenTheOrderIsAlreadyRejected()
    {
        Order order = GivenOrder(OrderStatus.Rejected);
        GivenLatestChange(order, OrderStatus.Rejected, PlacedAt.AddMinutes(1), "Sorry, we are out of chicken today.");

        await _service.ApplyCancelAsync(Message(order.Id));

        order.Status.ShouldBe(OrderStatus.Rejected);
        OrderStatusChanged sent = SentMessage();
        sent.NewStatus.ShouldBe(OrderStatus.Rejected);
        sent.Reason.ShouldBe("Sorry, we are out of chicken today.");
    }

    [Fact]
    public async Task ApplyCancelAsync_ShouldStillSendTheCurrentStatus_WhenTheOrderHasNoHistoryRowForIt()
    {
        Order order = GivenOrder(OrderStatus.Accepted);
        DateTime before = DateTime.UtcNow;

        await _service.ApplyCancelAsync(Message(order.Id));

        SentMessage().ChangedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public async Task ApplyCancelAsync_ShouldThrowSoTheMessageIsRetried_WhenTheOrderDoesNotExistYet()
    {
        OrderCancelled message = Message(Guid.NewGuid());

        OrderNotReceivedException exception = await Should.ThrowAsync<OrderNotReceivedException>(
            () => _service.ApplyCancelAsync(message));

        exception.OrderId.ShouldBe(message.OrderId);
        exception.MessageType.ShouldBe(nameof(OrderCancelled));
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private Order GivenOrder(OrderStatus status)
    {
        var order = new Order { Id = Guid.NewGuid(), Status = status, PlacedAt = PlacedAt, Total = 27.00m };
        _orders.GetOrderAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private void GivenLatestChange(Order order, OrderStatus status, DateTime changedAt, string? note = null) =>
        _orders.GetLatestStatusChangeAsync(order.Id, status, Arg.Any<CancellationToken>())
            .Returns(new OrderStatusHistory { Id = Guid.NewGuid(), OrderId = order.Id, Status = status, ChangedAt = changedAt, Note = note });

    private OrderStatusHistory AddedHistory() =>
        _orders.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IOrderRepository.AddHistory))
            .GetArguments()[0]
            .ShouldBeOfType<OrderStatusHistory>();

    private OrderStatusChanged SentMessage() =>
        _outbox.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IOutbox.Add))
            .GetArguments()[0]
            .ShouldBeOfType<OrderStatusChanged>();

    private static OrderCancelled Message(Guid orderId) => new()
    {
        CorrelationId = orderId,
        OrderId = orderId,
        Reason = "Ordered by mistake",
        CancelledAt = PlacedAt.AddMinutes(3)
    };
}
