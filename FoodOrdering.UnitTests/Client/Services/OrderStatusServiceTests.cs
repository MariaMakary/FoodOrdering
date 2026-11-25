using Client.Web.Hubs;
using Client.Web.Models.Entities;
using Client.Web.Repositories;
using Client.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shouldly;

namespace FoodOrdering.UnitTests.Client.Services;

public class OrderStatusServiceTests
{
    private static readonly DateTime PlacedAt = new(2026, 11, 24, 12, 30, 0, DateTimeKind.Utc);

    private readonly IOrderStatusRepository _orders = Substitute.For<IOrderStatusRepository>();
    private readonly IOrderNotifier _notifier = Substitute.For<IOrderNotifier>();
    private readonly OrderStatusService _service;

    public OrderStatusServiceTests()
    {
        _service = new OrderStatusService(_orders, _notifier, NullLogger<OrderStatusService>.Instance);
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldUpdateTheOrderAddAHistoryRowAndSave()
    {
        Order order = GivenOrder(OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = Message(order.Id, OrderStatus.Accepted, PlacedAt.AddMinutes(2), eta: PlacedAt.AddMinutes(45));

        await _service.ApplyStatusChangeAsync(message);

        order.Status.ShouldBe(OrderStatus.Accepted);
        order.EstimatedDeliveryTime.ShouldBe(PlacedAt.AddMinutes(45));
        order.LastStatusChangedAt.ShouldBe(message.ChangedAt);
        OrderStatusHistory history = AddedHistory();
        history.OrderId.ShouldBe(order.Id);
        history.Status.ShouldBe(OrderStatus.Accepted);
        history.ChangedAt.ShouldBe(message.ChangedAt);
        history.Note.ShouldBeNull();
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldKeepTheEta_WhenTheMessageHasNone()
    {
        Order order = GivenOrder(OrderStatus.Accepted, PlacedAt.AddMinutes(2));
        order.EstimatedDeliveryTime = PlacedAt.AddMinutes(45);

        await _service.ApplyStatusChangeAsync(Message(order.Id, OrderStatus.Preparing, PlacedAt.AddMinutes(5)));

        order.Status.ShouldBe(OrderStatus.Preparing);
        order.EstimatedDeliveryTime.ShouldBe(PlacedAt.AddMinutes(45));
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldSaveTheReason_WhenTheOrderIsRejected()
    {
        Order order = GivenOrder(OrderStatus.Placed, PlacedAt);

        await _service.ApplyStatusChangeAsync(Message(order.Id, OrderStatus.Rejected, PlacedAt.AddMinutes(2), reason: "Out of chicken"));

        order.Status.ShouldBe(OrderStatus.Rejected);
        order.RejectionReason.ShouldBe("Out of chicken");
        AddedHistory().Note.ShouldBe("Out of chicken");
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldDoNothing_WhenTheOrderDoesNotExist()
    {
        await _service.ApplyStatusChangeAsync(Message(Guid.NewGuid(), OrderStatus.Accepted, PlacedAt));

        _orders.DidNotReceive().AddHistory(Arg.Any<OrderStatusHistory>());
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ApplyStatusChangeAsync_ShouldDoNothing_WhenTheMessageIsNotNewerThanTheLastChange(int minutesAfterLastChange)
    {
        DateTime lastChange = PlacedAt.AddMinutes(5);
        Order order = GivenOrder(OrderStatus.Preparing, lastChange);

        await _service.ApplyStatusChangeAsync(Message(order.Id, OrderStatus.Accepted, lastChange.AddMinutes(minutesAfterLastChange), eta: PlacedAt.AddMinutes(45)));

        order.Status.ShouldBe(OrderStatus.Preparing);
        order.EstimatedDeliveryTime.ShouldBeNull();
        order.LastStatusChangedAt.ShouldBe(lastChange);
        _orders.DidNotReceive().AddHistory(Arg.Any<OrderStatusHistory>());
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldFollowTheAdmin_WhenTheClientShowsCancelled()
    {
        DateTime cancelledAt = PlacedAt.AddMinutes(3);
        Order order = GivenOrder(OrderStatus.Cancelled, cancelledAt);

        await _service.ApplyStatusChangeAsync(Message(order.Id, OrderStatus.Accepted, PlacedAt.AddMinutes(2), eta: PlacedAt.AddMinutes(45)));

        order.Status.ShouldBe(OrderStatus.Accepted);
        order.EstimatedDeliveryTime.ShouldBe(PlacedAt.AddMinutes(45));
        order.LastStatusChangedAt.ShouldBe(PlacedAt.AddMinutes(2));
        AddedHistory().Note.ShouldBe(OrderStatusService.CancelRefusedNote);
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldUseTheAdminReasonAsTheNote_WhenItOverridesACancel()
    {
        Order order = GivenOrder(OrderStatus.Cancelled, PlacedAt.AddMinutes(3));

        await _service.ApplyStatusChangeAsync(Message(order.Id, OrderStatus.Accepted, PlacedAt.AddMinutes(4), reason: "Already cooking"));

        AddedHistory().Note.ShouldBe("Already cooking");
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldNotifyTheOwnerOnceAfterSaving()
    {
        Order order = GivenOrder(OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = Message(order.Id, OrderStatus.Accepted, PlacedAt.AddMinutes(2), eta: PlacedAt.AddMinutes(45));

        await _service.ApplyStatusChangeAsync(message);

        await _notifier.Received(1).OrderStatusUpdatedAsync(
            order.CustomerId, order.Id, OrderStatus.Accepted, PlacedAt.AddMinutes(45), Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _orders.SaveChangesAsync(Arg.Any<CancellationToken>());
            _notifier.OrderStatusUpdatedAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<OrderStatus>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldNotifyWithTheKeptEta_WhenTheMessageHasNone()
    {
        Order order = GivenOrder(OrderStatus.Accepted, PlacedAt.AddMinutes(2));
        order.EstimatedDeliveryTime = PlacedAt.AddMinutes(45);

        await _service.ApplyStatusChangeAsync(Message(order.Id, OrderStatus.Preparing, PlacedAt.AddMinutes(5)));

        await _notifier.Received(1).OrderStatusUpdatedAsync(
            order.CustomerId, order.Id, OrderStatus.Preparing, PlacedAt.AddMinutes(45), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldNotNotify_WhenTheOrderDoesNotExist()
    {
        await _service.ApplyStatusChangeAsync(Message(Guid.NewGuid(), OrderStatus.Accepted, PlacedAt));

        await NotifierShouldNotBeCalledAsync();
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldNotNotify_WhenTheMessageIsStale()
    {
        Order order = GivenOrder(OrderStatus.Preparing, PlacedAt.AddMinutes(5));

        await _service.ApplyStatusChangeAsync(Message(order.Id, OrderStatus.Accepted, PlacedAt.AddMinutes(2)));

        await NotifierShouldNotBeCalledAsync();
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldNotifyOnlyOnce_WhenTheSameMessageArrivesTwice()
    {
        Order order = GivenOrder(OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = Message(order.Id, OrderStatus.Accepted, PlacedAt.AddMinutes(2), eta: PlacedAt.AddMinutes(45));

        await _service.ApplyStatusChangeAsync(message);
        await _service.ApplyStatusChangeAsync(message);

        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.Received(1).OrderStatusUpdatedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<OrderStatus>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyStatusChangeAsync_ShouldStillSucceed_WhenTheLiveUpdateFails()
    {
        Order order = GivenOrder(OrderStatus.Placed, PlacedAt);
        _notifier.OrderStatusUpdatedAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<OrderStatus>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Hub is down"));

        await Should.NotThrowAsync(() =>
            _service.ApplyStatusChangeAsync(Message(order.Id, OrderStatus.Accepted, PlacedAt.AddMinutes(2))));

        order.Status.ShouldBe(OrderStatus.Accepted);
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private Task NotifierShouldNotBeCalledAsync() =>
        _notifier.DidNotReceive().OrderStatusUpdatedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<OrderStatus>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());

    private Order GivenOrder(OrderStatus status, DateTime lastStatusChangedAt)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "FO-1042",
            CustomerId = Guid.NewGuid(),
            Status = status,
            LastStatusChangedAt = lastStatusChangedAt
        };
        _orders.GetOrderAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private OrderStatusHistory AddedHistory() =>
        _orders.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IOrderStatusRepository.AddHistory))
            .GetArguments()[0]
            .ShouldBeOfType<OrderStatusHistory>();

    private static OrderStatusChanged Message(
        Guid orderId,
        OrderStatus status,
        DateTime changedAt,
        DateTime? eta = null,
        string? reason = null) => new()
    {
        CorrelationId = orderId,
        OrderId = orderId,
        NewStatus = status,
        EstimatedDeliveryTime = eta,
        Reason = reason,
        ChangedAt = changedAt
    };
}
