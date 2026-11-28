using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Client.Web.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;
using Shouldly;

namespace FoodOrdering.UnitTests.Client.Services;

public class OrderServiceTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICartRepository _carts = Substitute.For<ICartRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly OrderService _service;
    private readonly ApplicationUser _customer;
    private readonly Cart _cart;
    private readonly MenuItem _burger;
    private readonly MenuItem _lemonade;
    private readonly MenuItemExtra _cheese;
    private readonly MenuItemExtra _bacon;
    private Order? _addedOrder;
    private OrderPlaced? _message;

    public OrderServiceTests()
    {
        _service = new OrderService(_orders, _carts, _outbox);

        _customer = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            FirstName = "Sara",
            LastName = "Haddad",
            PhoneNumber = "+961 70 123 456",
            DefaultAddress = "Hamra Street 12, Beirut"
        };

        _burger = new MenuItem { Id = Guid.NewGuid(), Name = "Chicken Burger", Price = 9.50m, IsAvailable = true };
        _cheese = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = _burger.Id, Name = "Extra cheese", Price = 1.00m };
        _bacon = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = _burger.Id, Name = "Bacon", Price = 1.50m };
        _lemonade = new MenuItem { Id = Guid.NewGuid(), Name = "Lemonade", Price = 3.00m, IsAvailable = true };

        _cart = new Cart { Id = Guid.NewGuid(), CustomerId = _customer.Id };
        _cart.Items.Add(NewCartItem(_burger, 2, _cheese, _bacon));
        _cart.Items.Add(NewCartItem(_lemonade, 1));

        _orders.GetCustomerAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        _orders.NextOrderNumberAsync(Arg.Any<CancellationToken>()).Returns(1042L);
        _orders.Add(Arg.Do<Order>(o => _addedOrder = o));
        _carts.GetByCustomerAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_cart);
        _outbox.Add(Arg.Do<MessageBase>(m => _message = m as OrderPlaced));
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldCreateAPlacedOrderWithItemsAndExtras()
    {
        ServiceResult<Guid> result = await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        result.Succeeded.ShouldBeTrue();
        _addedOrder.ShouldNotBeNull();
        result.Value.ShouldBe(_addedOrder.Id);
        _addedOrder.Id.ShouldNotBe(Guid.Empty);
        _addedOrder.OrderNumber.ShouldBe("FO-1042");
        _addedOrder.CustomerId.ShouldBe(_customer.Id);
        _addedOrder.Status.ShouldBe(OrderStatus.Placed);
        _addedOrder.DeliveryType.ShouldBe(DeliveryType.Delivery);
        _addedOrder.Address.ShouldBe("Hamra Street 12, Beirut");
        _addedOrder.Notes.ShouldBe("Please ring twice");

        OrderItem burgerLine = _addedOrder.Items.Single(i => i.MenuItemId == _burger.Id);
        burgerLine.Name.ShouldBe("Chicken Burger");
        burgerLine.UnitPrice.ShouldBe(9.50m);
        burgerLine.Quantity.ShouldBe(2);
        burgerLine.Extras.Select(e => (e.Name, e.Price)).ShouldBe([("Bacon", 1.50m), ("Extra cheese", 1.00m)]);

        OrderItem lemonadeLine = _addedOrder.Items.Single(i => i.MenuItemId == _lemonade.Id);
        lemonadeLine.UnitPrice.ShouldBe(3.00m);
        lemonadeLine.Extras.ShouldBeEmpty();

        _addedOrder.Total.ShouldBe(27.00m);
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldUsePricesFromTheDatabaseNotFromTheViewModel()
    {
        CheckoutViewModel checkout = NewCheckout();
        checkout.Cart = new CartViewModel
        {
            Items =
            [
                new CartItemViewModel { MenuItemId = _burger.Id, Name = "Chicken Burger", Quantity = 2, UnitPrice = 0.01m, LineTotal = 0.02m },
                new CartItemViewModel { MenuItemId = _lemonade.Id, Name = "Lemonade", Quantity = 1, UnitPrice = 0.01m, LineTotal = 0.01m }
            ],
            Subtotal = 0.03m
        };

        await _service.PlaceOrderAsync(_customer.Id, checkout);

        _addedOrder.ShouldNotBeNull();
        _addedOrder.Items.Single(i => i.MenuItemId == _burger.Id).UnitPrice.ShouldBe(9.50m);
        _addedOrder.Total.ShouldBe(27.00m);
        _message.ShouldNotBeNull();
        _message.Total.ShouldBe(27.00m);
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldAddOneOrderPlacedToTheOutbox()
    {
        await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        _outbox.Received(1).Add(Arg.Any<OrderPlaced>());
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldPutTheFullSnapshotInTheMessage()
    {
        await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        _addedOrder.ShouldNotBeNull();
        _message.ShouldNotBeNull();
        _message.OrderId.ShouldBe(_addedOrder.Id);
        _message.CorrelationId.ShouldBe(_addedOrder.Id);
        _message.OrderNumber.ShouldBe("FO-1042");
        _message.CustomerId.ShouldBe(_customer.Id);
        _message.CustomerName.ShouldBe("Sara Haddad");
        _message.CustomerPhone.ShouldBe("+961 70 123 456");
        _message.DeliveryType.ShouldBe(DeliveryType.Delivery);
        _message.Address.ShouldBe("Hamra Street 12, Beirut");
        _message.Notes.ShouldBe("Please ring twice");
        _message.PaymentMethod.ShouldBe(PaymentMethod.Online);
        _message.Items.Count.ShouldBe(2);
        OrderItemDto burgerLine = _message.Items.Single(i => i.MenuItemId == _burger.Id);
        burgerLine.Name.ShouldBe("Chicken Burger");
        burgerLine.UnitPrice.ShouldBe(9.50m);
        burgerLine.Quantity.ShouldBe(2);
        burgerLine.Extras.Select(e => (e.Name, e.Price)).ShouldBe([("Bacon", 1.50m), ("Extra cheese", 1.00m)]);
        _message.Total.ShouldBe(27.00m);
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldUseOneTimeForTheStatusTheHistoryAndTheMessage()
    {
        DateTime before = DateTime.UtcNow;

        await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        _addedOrder.ShouldNotBeNull();
        _message.ShouldNotBeNull();
        OrderStatusHistory history = _addedOrder.StatusHistory.ShouldHaveSingleItem();
        history.Status.ShouldBe(OrderStatus.Placed);
        history.ChangedAt.ShouldBe(_addedOrder.LastStatusChangedAt);
        _message.PlacedAt.ShouldBe(_addedOrder.LastStatusChangedAt);
        _addedOrder.LastStatusChangedAt.ShouldBeGreaterThanOrEqualTo(before);
        _addedOrder.LastStatusChangedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldClearTheCartAndSaveOnce()
    {
        await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        Received.InOrder(() =>
        {
            _orders.Add(Arg.Any<Order>());
            _carts.ClearItems(_cart);
            _outbox.Add(Arg.Any<OrderPlaced>());
            _orders.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _carts.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PaymentMethod.Online, PaymentStatus.Unpaid)]
    [InlineData(PaymentMethod.Cash, PaymentStatus.CashOnDelivery)]
    public async Task PlaceOrderAsync_ShouldSetThePaymentStatusFromThePaymentMethod(PaymentMethod method, PaymentStatus expected)
    {
        CheckoutViewModel checkout = NewCheckout();
        checkout.PaymentMethod = method;

        await _service.PlaceOrderAsync(_customer.Id, checkout);

        _addedOrder.ShouldNotBeNull();
        _addedOrder.PaymentMethod.ShouldBe(method);
        _addedOrder.PaymentStatus.ShouldBe(expected);
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldDropTheAddress_WhenTheOrderIsForPickup()
    {
        CheckoutViewModel checkout = NewCheckout();
        checkout.DeliveryType = DeliveryType.Pickup;

        ServiceResult<Guid> result = await _service.PlaceOrderAsync(_customer.Id, checkout);

        result.Succeeded.ShouldBeTrue();
        _addedOrder.ShouldNotBeNull();
        _addedOrder.DeliveryType.ShouldBe(DeliveryType.Pickup);
        _addedOrder.Address.ShouldBeNull();
        _message.ShouldNotBeNull();
        _message.Address.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PlaceOrderAsync_ShouldFail_WhenADeliveryHasNoAddress(string? address)
    {
        CheckoutViewModel checkout = NewCheckout();
        checkout.Address = address;

        ServiceResult<Guid> result = await _service.PlaceOrderAsync(_customer.Id, checkout);

        result.Error.ShouldBe(OrderErrors.AddressRequired);
        await AssertNothingWasPlacedAsync();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldFail_WhenTheCartIsEmpty()
    {
        _cart.Items.Clear();

        ServiceResult<Guid> result = await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe(OrderErrors.EmptyCart);
        await AssertNothingWasPlacedAsync();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldFail_WhenTheCustomerHasNoCart()
    {
        _carts.GetByCustomerAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns((Cart?)null);

        ServiceResult<Guid> result = await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        result.Error.ShouldBe(OrderErrors.EmptyCart);
        await AssertNothingWasPlacedAsync();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldFail_WhenADishInTheCartIsSoldOut()
    {
        _lemonade.IsAvailable = false;

        ServiceResult<Guid> result = await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe(OrderErrors.UnavailableDishesCode);
        result.Error.Message.ShouldContain("Lemonade");
        await AssertNothingWasPlacedAsync();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldFail_WhenADishInTheCartWasDeleted()
    {
        _burger.IsDeleted = true;

        ServiceResult<Guid> result = await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe(OrderErrors.UnavailableDishesCode);
        result.Error.Message.ShouldContain("Chicken Burger");
        await AssertNothingWasPlacedAsync();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldFail_WhenTheCustomerNoLongerExists()
    {
        _orders.GetCustomerAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);

        ServiceResult<Guid> result = await _service.PlaceOrderAsync(_customer.Id, NewCheckout());

        result.Error.ShouldBe(OrderErrors.CustomerNotFound);
        await AssertNothingWasPlacedAsync();
    }

    [Fact]
    public async Task GetCheckoutAsync_ShouldPrefillTheAddressFromTheDefaultAddress()
    {
        ServiceResult<CheckoutViewModel> result = await _service.GetCheckoutAsync(_customer.Id);

        result.Succeeded.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Address.ShouldBe("Hamra Street 12, Beirut");
        result.Value.DeliveryType.ShouldBe(DeliveryType.Delivery);
        result.Value.Cart.Items.Count.ShouldBe(2);
        result.Value.Cart.Subtotal.ShouldBe(27.00m);
    }

    [Fact]
    public async Task GetCheckoutAsync_ShouldFail_WhenTheCartIsEmpty()
    {
        _cart.Items.Clear();

        ServiceResult<CheckoutViewModel> result = await _service.GetCheckoutAsync(_customer.Id);

        result.Error.ShouldBe(OrderErrors.EmptyCart);
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldCancelTheOrderAndAddOneOrderCancelledToTheOutbox()
    {
        Order order = GivenOrder(OrderStatus.Placed);
        OrderStatusHistory? history = null;
        _orders.AddHistory(Arg.Do<OrderStatusHistory>(h => history = h));
        OrderCancelled? cancelled = null;
        _outbox.Add(Arg.Do<MessageBase>(m => cancelled = m as OrderCancelled));
        DateTime before = DateTime.UtcNow;

        ServiceResult result = await _service.CancelOrderAsync(order.Id, _customer.Id);

        result.Succeeded.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.LastStatusChangedAt.ShouldBeGreaterThanOrEqualTo(before);
        history.ShouldNotBeNull();
        history.OrderId.ShouldBe(order.Id);
        history.Status.ShouldBe(OrderStatus.Cancelled);
        history.ChangedAt.ShouldBe(order.LastStatusChangedAt);
        _outbox.Received(1).Add(Arg.Any<MessageBase>());
        cancelled.ShouldNotBeNull();
        cancelled.OrderId.ShouldBe(order.Id);
        cancelled.CorrelationId.ShouldBe(order.Id);
        cancelled.Reason.ShouldBe("Cancelled by customer");
        cancelled.CancelledAt.ShouldBe(order.LastStatusChangedAt);
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task CancelOrderAsync_ShouldRefuse_WhenTheOrderIsNoLongerPlaced(OrderStatus status)
    {
        Order order = GivenOrder(status);

        ServiceResult result = await _service.CancelOrderAsync(order.Id, _customer.Id);

        result.Error.ShouldBe(OrderErrors.CannotCancel);
        order.Status.ShouldBe(status);
        await AssertNothingWasCancelledAsync();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReturnNotFound_WhenTheOrderBelongsToAnotherCustomer()
    {
        Order order = GivenOrder(OrderStatus.Placed, Guid.NewGuid());

        ServiceResult result = await _service.CancelOrderAsync(order.Id, _customer.Id);

        result.Error.ShouldBe(OrderErrors.OrderNotFound);
        order.Status.ShouldBe(OrderStatus.Placed);
        await AssertNothingWasCancelledAsync();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReturnNotFound_WhenTheOrderDoesNotExist()
    {
        ServiceResult result = await _service.CancelOrderAsync(Guid.NewGuid(), _customer.Id);

        result.Error.ShouldBe(OrderErrors.OrderNotFound);
        await AssertNothingWasCancelledAsync();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldFailAndDiscardTheChanges_WhenTheRestaurantChangedTheOrderAtTheSameTime()
    {
        Order order = GivenOrder(OrderStatus.Placed);
        _orders.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new DbUpdateConcurrencyException("The order was changed by another writer.")));

        ServiceResult result = await _service.CancelOrderAsync(order.Id, _customer.Id);

        result.Error.ShouldNotBeNull();
        result.Error.ShouldBe(OrderErrors.ChangedByRestaurant);
        result.Error.Message.ShouldStartWith("The restaurant has just updated your order");
        Received.InOrder(() =>
        {
            _orders.SaveChangesAsync(Arg.Any<CancellationToken>());
            _orders.DiscardChanges();
        });
    }

    [Fact]
    public async Task GetCustomerOrdersAsync_ShouldMapEveryOrderInTheOrderTheRepositoryReturns()
    {
        Order newer = NewOrder(OrderStatus.Accepted, _customer.Id, "FO-1001");
        Order older = NewOrder(OrderStatus.Delivered, _customer.Id, "FO-1000");
        _orders.GetCustomerOrdersAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns([newer, older]);

        IReadOnlyList<OrderListItemViewModel> result = await _service.GetCustomerOrdersAsync(_customer.Id);

        result.Select(o => o.OrderNumber).ShouldBe(["FO-1001", "FO-1000"]);
        OrderListItemViewModel first = result[0];
        first.Id.ShouldBe(newer.Id);
        first.Total.ShouldBe(newer.Total);
        first.Status.ShouldBe(OrderStatus.Accepted);
        first.PaymentStatus.ShouldBe(PaymentStatus.CashOnDelivery);
    }

    [Fact]
    public async Task GetOrderDetailsAsync_ShouldReturnNull_WhenTheOrderBelongsToAnotherCustomer()
    {
        Order order = NewOrder(OrderStatus.Placed, Guid.NewGuid());
        _orders.GetOrderWithDetailsAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        OrderDetailsViewModel? result = await _service.GetOrderDetailsAsync(order.Id, _customer.Id);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetOrderDetailsAsync_ShouldMapItemsExtrasAndHistory()
    {
        Order order = NewDetailedOrder(OrderStatus.Placed);

        OrderDetailsViewModel? result = await _service.GetOrderDetailsAsync(order.Id, _customer.Id);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(order.Id);
        result.OrderNumber.ShouldBe(order.OrderNumber);
        result.Items.Count.ShouldBe(2);
        OrderItemViewModel burger = result.Items.Single(i => i.MenuItemId == _burger.Id);
        burger.Extras.Select(e => (e.Name, e.Price)).ShouldBe([("Bacon", 1.50m), ("Extra cheese", 1.00m)]);
        burger.LineTotal.ShouldBe(24.00m);
        result.StatusHistory.Select(h => h.Status).ShouldBe([OrderStatus.Placed]);
        result.CanCancel.ShouldBeTrue();
        result.CanRate.ShouldBeFalse();
    }

    [Theory]
    [InlineData(OrderStatus.Placed, true)]
    [InlineData(OrderStatus.Accepted, false)]
    [InlineData(OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    public async Task GetOrderDetailsAsync_ShouldAllowCancelOnlyWhilePlaced(OrderStatus status, bool canCancel)
    {
        Order order = NewDetailedOrder(status);

        OrderDetailsViewModel? result = await _service.GetOrderDetailsAsync(order.Id, _customer.Id);

        result.ShouldNotBeNull();
        result.CanCancel.ShouldBe(canCancel);
    }

    [Fact]
    public async Task GetOrderDetailsAsync_ShouldAllowRating_WhenTheOrderIsDeliveredAndADishIsNotRatedYet()
    {
        Order order = NewDetailedOrder(OrderStatus.Delivered);
        order.Ratings.Add(new Rating { Id = Guid.NewGuid(), OrderId = order.Id, MenuItemId = _burger.Id, Stars = 5 });

        OrderDetailsViewModel? result = await _service.GetOrderDetailsAsync(order.Id, _customer.Id);

        result.ShouldNotBeNull();
        result.CanRate.ShouldBeTrue();
        result.Items.Single(i => i.MenuItemId == _burger.Id).IsRated.ShouldBeTrue();
        result.Items.Single(i => i.MenuItemId == _lemonade.Id).IsRated.ShouldBeFalse();
    }

    [Fact]
    public async Task GetOrderDetailsAsync_ShouldNotAllowRating_WhenEveryDishIsRated()
    {
        Order order = NewDetailedOrder(OrderStatus.Delivered);
        order.Ratings.Add(new Rating { Id = Guid.NewGuid(), OrderId = order.Id, MenuItemId = _burger.Id, Stars = 5 });
        order.Ratings.Add(new Rating { Id = Guid.NewGuid(), OrderId = order.Id, MenuItemId = _lemonade.Id, Stars = 4 });

        OrderDetailsViewModel? result = await _service.GetOrderDetailsAsync(order.Id, _customer.Id);

        result.ShouldNotBeNull();
        result.CanRate.ShouldBeFalse();
    }

    [Fact]
    public async Task GetConfirmationAsync_ShouldMapTheOrderNumberItemsAndTotal()
    {
        Order order = NewDetailedOrder(OrderStatus.Accepted);
        order.EstimatedDeliveryTime = new DateTime(2026, 11, 24, 13, 15, 0, DateTimeKind.Utc);

        OrderConfirmationViewModel? result = await _service.GetConfirmationAsync(order.Id, _customer.Id);

        result.ShouldNotBeNull();
        result.OrderId.ShouldBe(order.Id);
        result.OrderNumber.ShouldBe("FO-1000");
        result.Total.ShouldBe(27.00m);
        result.DeliveryType.ShouldBe(DeliveryType.Delivery);
        result.Address.ShouldBe("Hamra Street 12, Beirut");
        result.PaymentStatus.ShouldBe(PaymentStatus.CashOnDelivery);
        result.Status.ShouldBe(OrderStatus.Accepted);
        result.EstimatedTime.ShouldBe(order.EstimatedDeliveryTime);
        result.Items.Count.ShouldBe(2);
        OrderItemViewModel burger = result.Items.Single(i => i.MenuItemId == _burger.Id);
        burger.Quantity.ShouldBe(2);
        burger.Extras.Select(e => (e.Name, e.Price)).ShouldBe([("Bacon", 1.50m), ("Extra cheese", 1.00m)]);
        burger.LineTotal.ShouldBe(24.00m);
        result.Items.Single(i => i.MenuItemId == _lemonade.Id).LineTotal.ShouldBe(3.00m);
    }

    [Theory]
    [InlineData(PaymentStatus.CashOnDelivery)]
    [InlineData(PaymentStatus.Unpaid)]
    [InlineData(PaymentStatus.Paid)]
    public async Task GetConfirmationAsync_ShouldReturnThePaymentStatusOfTheOrder(PaymentStatus status)
    {
        Order order = NewDetailedOrder(OrderStatus.Placed);
        order.PaymentMethod = status == PaymentStatus.CashOnDelivery ? PaymentMethod.Cash : PaymentMethod.Online;
        order.PaymentStatus = status;

        OrderConfirmationViewModel? result = await _service.GetConfirmationAsync(order.Id, _customer.Id);

        result.ShouldNotBeNull();
        result.PaymentStatus.ShouldBe(status);
    }

    [Fact]
    public async Task GetConfirmationAsync_ShouldReturnNull_WhenTheOrderBelongsToAnotherCustomer()
    {
        Order order = NewOrder(OrderStatus.Placed, Guid.NewGuid());
        _orders.GetOrderWithDetailsAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        OrderConfirmationViewModel? result = await _service.GetConfirmationAsync(order.Id, _customer.Id);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetConfirmationAsync_ShouldReturnNull_WhenTheOrderDoesNotExist()
    {
        OrderConfirmationViewModel? result = await _service.GetConfirmationAsync(Guid.NewGuid(), _customer.Id);

        result.ShouldBeNull();
    }

    private Order GivenOrder(OrderStatus status, Guid? customerId = null)
    {
        Order order = NewOrder(status, customerId ?? _customer.Id);
        _orders.GetOrderForUpdateAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private Order NewDetailedOrder(OrderStatus status)
    {
        Order order = NewOrder(status, _customer.Id);
        Guid burgerLineId = Guid.NewGuid();
        order.Items =
        [
            new OrderItem
            {
                Id = burgerLineId,
                OrderId = order.Id,
                MenuItemId = _burger.Id,
                Name = "Chicken Burger",
                UnitPrice = 9.50m,
                Quantity = 2,
                Extras =
                [
                    new OrderItemExtra { Id = Guid.NewGuid(), OrderItemId = burgerLineId, Name = "Extra cheese", Price = 1.00m },
                    new OrderItemExtra { Id = Guid.NewGuid(), OrderItemId = burgerLineId, Name = "Bacon", Price = 1.50m }
                ]
            },
            new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, MenuItemId = _lemonade.Id, Name = "Lemonade", UnitPrice = 3.00m, Quantity = 1 }
        ];
        order.StatusHistory =
        [
            new OrderStatusHistory { Id = Guid.NewGuid(), OrderId = order.Id, Status = OrderStatus.Placed, ChangedAt = order.LastStatusChangedAt }
        ];
        _orders.GetOrderWithDetailsAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private static Order NewOrder(OrderStatus status, Guid customerId, string orderNumber = "FO-1000") => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = orderNumber,
        CustomerId = customerId,
        DeliveryType = DeliveryType.Delivery,
        Address = "Hamra Street 12, Beirut",
        PaymentMethod = PaymentMethod.Cash,
        PaymentStatus = PaymentStatus.CashOnDelivery,
        Status = status,
        Total = 27.00m,
        LastStatusChangedAt = new DateTime(2026, 11, 24, 12, 30, 0, DateTimeKind.Utc)
    };

    private async Task AssertNothingWasCancelledAsync()
    {
        _orders.DidNotReceive().AddHistory(Arg.Any<OrderStatusHistory>());
        _outbox.DidNotReceive().Add(Arg.Any<MessageBase>());
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private async Task AssertNothingWasPlacedAsync()
    {
        _orders.DidNotReceive().Add(Arg.Any<Order>());
        _carts.DidNotReceive().ClearItems(Arg.Any<Cart>());
        _outbox.DidNotReceive().Add(Arg.Any<MessageBase>());
        await _orders.DidNotReceive().NextOrderNumberAsync(Arg.Any<CancellationToken>());
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static CheckoutViewModel NewCheckout() => new()
    {
        DeliveryType = DeliveryType.Delivery,
        Address = " Hamra Street 12, Beirut ",
        Notes = "Please ring twice",
        PaymentMethod = PaymentMethod.Online
    };

    private CartItem NewCartItem(MenuItem dish, int quantity, params MenuItemExtra[] extras)
    {
        Guid itemId = Guid.NewGuid();
        return new CartItem
        {
            Id = itemId,
            CartId = _cart.Id,
            MenuItemId = dish.Id,
            MenuItem = dish,
            Quantity = quantity,
            Extras = extras
                .Select(e => new CartItemExtra { CartItemId = itemId, MenuItemExtraId = e.Id, MenuItemExtra = e })
                .ToList()
        };
    }
}
