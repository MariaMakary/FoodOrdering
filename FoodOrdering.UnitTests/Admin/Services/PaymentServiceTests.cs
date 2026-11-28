using Admin.Web.Models.Entities;
using Admin.Web.Repositories;
using Admin.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shouldly;

namespace FoodOrdering.UnitTests.Admin.Services;

public class PaymentServiceTests
{
    private static readonly DateTime PaidAt = new(2026, 11, 24, 12, 30, 18, DateTimeKind.Utc);

    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _service = new PaymentService(_payments, NullLogger<PaymentService>.Instance);
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldAddAPaymentWithTheMessageIdsMarkTheOrderPaidAndSave()
    {
        Order order = GivenOrder();
        PaymentCompleted message = Message(order.Id, 27.00m);

        await _service.RecordPaymentAsync(message);

        Payment payment = AddedPayment();
        payment.Id.ShouldBe(message.PaymentId);
        payment.OrderId.ShouldBe(order.Id);
        payment.Amount.ShouldBe(27.00m);
        payment.Method.ShouldBe(PaymentMethod.Online);
        payment.PaidAt.ShouldBe(PaidAt);
        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        await _payments.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldStillRecordThePayment_WhenTheAmountDiffersFromTheTotal()
    {
        Order order = GivenOrder();

        await _service.RecordPaymentAsync(Message(order.Id, 25.00m));

        AddedPayment().Amount.ShouldBe(25.00m);
        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldThrowSoTheMessageIsRetried_WhenTheOrderDoesNotExistYet()
    {
        PaymentCompleted message = Message(Guid.NewGuid(), 27.00m);

        OrderNotReceivedException exception = await Should.ThrowAsync<OrderNotReceivedException>(
            () => _service.RecordPaymentAsync(message));

        exception.OrderId.ShouldBe(message.OrderId);
        exception.MessageType.ShouldBe(nameof(PaymentCompleted));
        _payments.DidNotReceive().Add(Arg.Any<Payment>());
        await _payments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldDoNothing_WhenThePaymentIsAlreadySaved()
    {
        Order order = GivenOrder();
        PaymentCompleted message = Message(order.Id, 27.00m);
        order.Payment = new Payment { Id = message.PaymentId, OrderId = order.Id, Amount = 27.00m };
        order.PaymentStatus = PaymentStatus.Paid;

        await _service.RecordPaymentAsync(message);

        _payments.DidNotReceive().Add(Arg.Any<Payment>());
        await _payments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldDoNothing_WhenTheOrderAlreadyHasAnotherPayment()
    {
        Order order = GivenOrder();
        order.Payment = new Payment { Id = Guid.NewGuid(), OrderId = order.Id, Amount = 27.00m };

        await _service.RecordPaymentAsync(Message(order.Id, 27.00m));

        _payments.DidNotReceive().Add(Arg.Any<Payment>());
        await _payments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Rejected)]
    public async Task RecordPaymentAsync_ShouldMarkTheOrderPaid_WhenTheOrderIsAlreadyFinal(OrderStatus status)
    {
        Order order = GivenOrder();
        order.Status = status;

        await _service.RecordPaymentAsync(Message(order.Id, 27.00m));

        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        order.Status.ShouldBe(status);
        AddedPayment().OrderId.ShouldBe(order.Id);
    }

    private Order GivenOrder()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            PaymentMethod = PaymentMethod.Online,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = OrderStatus.Placed,
            Total = 27.00m
        };
        _payments.GetOrderWithPaymentAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private Payment AddedPayment() =>
        _payments.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IPaymentRepository.Add))
            .GetArguments()[0]
            .ShouldBeOfType<Payment>();

    private static PaymentCompleted Message(Guid orderId, decimal amount) => new()
    {
        CorrelationId = orderId,
        OrderId = orderId,
        PaymentId = Guid.NewGuid(),
        Amount = amount,
        Method = PaymentMethod.Online,
        PaidAt = PaidAt
    };
}
