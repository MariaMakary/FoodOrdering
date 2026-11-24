using Admin.Web.Models.Entities;
using Admin.Web.Repositories;
using Admin.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shouldly;

namespace FoodOrdering.UnitTests.Admin.Services;

public class OrderServiceTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _service = new OrderService(_orders, NullLogger<OrderService>.Instance);
    }

    [Fact]
    public async Task CreateFromMessageAsync_ShouldAddAPlacedOrderWithTheSameIdAndSave()
    {
        OrderPlaced message = CreateMessage(PaymentMethod.Online);

        await _service.CreateFromMessageAsync(message);

        Order order = AddedOrder();
        order.Id.ShouldBe(message.OrderId);
        order.OrderNumber.ShouldBe(message.OrderNumber);
        order.CustomerName.ShouldBe(message.CustomerName);
        order.Address.ShouldBe(message.Address);
        order.Status.ShouldBe(OrderStatus.Placed);
        order.Total.ShouldBe(message.Total);
        order.PlacedAt.ShouldBe(message.PlacedAt);
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateFromMessageAsync_ShouldCopyEveryItemAndExtra()
    {
        OrderPlaced message = CreateMessage(PaymentMethod.Online);

        await _service.CreateFromMessageAsync(message);

        OrderItem item = AddedOrder().Items.ShouldHaveSingleItem();
        item.MenuItemId.ShouldBe(message.Items[0].MenuItemId);
        item.Name.ShouldBe("Chicken Burger");
        item.UnitPrice.ShouldBe(9.50m);
        item.Quantity.ShouldBe(2);
        item.Extras.Select(e => (e.Name, e.Price)).ShouldBe([("Extra cheese", 1.00m), ("Bacon", 1.50m)]);
    }

    [Fact]
    public async Task CreateFromMessageAsync_ShouldAddOnePlacedStatusHistoryRowAtThePlacedTime()
    {
        OrderPlaced message = CreateMessage(PaymentMethod.Online);

        await _service.CreateFromMessageAsync(message);

        OrderStatusHistory history = AddedOrder().StatusHistory.ShouldHaveSingleItem();
        history.Status.ShouldBe(OrderStatus.Placed);
        history.ChangedAt.ShouldBe(message.PlacedAt);
        history.ChangedByUserId.ShouldBeNull();
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, PaymentStatus.CashOnDelivery)]
    [InlineData(PaymentMethod.Online, PaymentStatus.Unpaid)]
    public async Task CreateFromMessageAsync_ShouldSetThePaymentStatusFromThePaymentMethod(PaymentMethod method, PaymentStatus expected)
    {
        await _service.CreateFromMessageAsync(CreateMessage(method));

        AddedOrder().PaymentStatus.ShouldBe(expected);
    }

    [Fact]
    public async Task CreateFromMessageAsync_ShouldDoNothing_WhenTheOrderAlreadyExists()
    {
        OrderPlaced message = CreateMessage(PaymentMethod.Cash);
        _orders.ExistsAsync(message.OrderId, Arg.Any<CancellationToken>()).Returns(true);

        await _service.CreateFromMessageAsync(message);

        _orders.DidNotReceive().Add(Arg.Any<Order>());
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private Order AddedOrder() =>
        _orders.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IOrderRepository.Add))
            .GetArguments()[0]
            .ShouldBeOfType<Order>();

    private static OrderPlaced CreateMessage(PaymentMethod paymentMethod)
    {
        Guid orderId = Guid.NewGuid();
        return new OrderPlaced
        {
            CorrelationId = orderId,
            OrderId = orderId,
            OrderNumber = "FO-1042",
            CustomerId = Guid.NewGuid(),
            CustomerName = "Sara Haddad",
            CustomerPhone = "+961 70 123 456",
            DeliveryType = DeliveryType.Delivery,
            Address = "Hamra Street, Beirut",
            Notes = "Please ring twice",
            PaymentMethod = paymentMethod,
            Items =
            [
                new OrderItemDto
                {
                    MenuItemId = Guid.NewGuid(),
                    Name = "Chicken Burger",
                    UnitPrice = 9.50m,
                    Quantity = 2,
                    Extras = [new ExtraDto { Name = "Extra cheese", Price = 1.00m }, new ExtraDto { Name = "Bacon", Price = 1.50m }]
                }
            ],
            Total = 24.00m,
            PlacedAt = new DateTime(2026, 11, 24, 12, 30, 0, DateTimeKind.Utc)
        };
    }
}
