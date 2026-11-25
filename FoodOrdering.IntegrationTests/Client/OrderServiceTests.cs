using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Client.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class OrderServiceTests(PostgresFixture postgres)
{
    private readonly ClientMenuSeed _seed = new();
    private readonly SaveCounter _saves = new();

    [Fact]
    public async Task PlaceOrderAsync_ShouldSaveTheOrderClearTheCartAndAddTheOutboxRowInOneSave()
    {
        string connectionString = await SeedWithCartAsync();
        Guid orderId;

        await using (ClientDbContext context = NewCountingContext(connectionString))
        {
            ServiceResult<Guid> result = await NewService(context).PlaceOrderAsync(_seed.Sara.Id, NewCheckout(PaymentMethod.Cash));
            result.Succeeded.ShouldBeTrue();
            orderId = result.Value;
        }

        _saves.Count.ShouldBe(1);

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        Order order = await check.Orders
            .Include(o => o.Items).ThenInclude(i => i.Extras)
            .Include(o => o.StatusHistory)
            .SingleAsync();
        order.Id.ShouldBe(orderId);
        order.OrderNumber.ShouldBe("FO-1000");
        order.Status.ShouldBe(OrderStatus.Placed);
        order.PaymentStatus.ShouldBe(PaymentStatus.CashOnDelivery);
        order.Address.ShouldBe("Hamra Street 12, Beirut");
        order.Total.ShouldBe(27.00m);
        order.CreatedAt.ShouldNotBe(default);
        order.Items.Count.ShouldBe(2);
        order.Items.Single(i => i.MenuItemId == _seed.ChickenBurger.Id).Extras
            .Select(e => (e.Name, e.Price)).OrderBy(e => e.Name).ShouldBe([("Bacon", 1.50m), ("Extra cheese", 1.00m)]);
        OrderStatusHistory history = order.StatusHistory.ShouldHaveSingleItem();
        history.Status.ShouldBe(OrderStatus.Placed);
        history.ChangedAt.ShouldBe(order.LastStatusChangedAt);

        (await check.CartItems.CountAsync()).ShouldBe(0);
        (await check.CartItemExtras.CountAsync()).ShouldBe(0);

        OutboxMessage row = await check.Set<OutboxMessage>().SingleAsync();
        row.Type.ShouldBe(nameof(OrderPlaced));
        row.CorrelationId.ShouldBe(orderId);
        OrderPlaced message = row.ToMessage().ShouldBeOfType<OrderPlaced>();
        message.OrderId.ShouldBe(orderId);
        message.OrderNumber.ShouldBe("FO-1000");
        message.Total.ShouldBe(27.00m);
        message.PlacedAt.ShouldBe(order.LastStatusChangedAt, TimeSpan.FromMilliseconds(1));
        message.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldUseTheCurrentDatabasePrices_WhenTheMenuChangedAfterAdding()
    {
        string connectionString = await SeedWithCartAsync();
        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            await context.MenuItems.Where(m => m.Id == _seed.ChickenBurger.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Price, 12.00m));
            await context.MenuItemExtras.Where(e => e.Id == _seed.Cheese.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Price, 2.00m));
        }

        CheckoutViewModel checkout = NewCheckout(PaymentMethod.Online);
        checkout.Cart = new CartViewModel { Subtotal = 0.01m };

        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            (await NewService(context).PlaceOrderAsync(_seed.Sara.Id, checkout)).Succeeded.ShouldBeTrue();
        }

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        Order order = await check.Orders.Include(o => o.Items).ThenInclude(i => i.Extras).SingleAsync();
        OrderItem burger = order.Items.Single(i => i.MenuItemId == _seed.ChickenBurger.Id);
        burger.UnitPrice.ShouldBe(12.00m);
        burger.Extras.Single(e => e.Name == "Extra cheese").Price.ShouldBe(2.00m);
        order.Total.ShouldBe(34.00m);
        order.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldSaveNothing_WhenADishInTheCartIsSoldOut()
    {
        string connectionString = await SeedWithCartAsync();
        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            await context.MenuItems.Where(m => m.Id == _seed.Lemonade.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsAvailable, false));
        }

        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            ServiceResult<Guid> result = await NewService(context).PlaceOrderAsync(_seed.Sara.Id, NewCheckout(PaymentMethod.Cash));
            result.Error.ShouldNotBeNull();
            result.Error.Code.ShouldBe(OrderErrors.UnavailableDishesCode);
        }

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await check.Orders.CountAsync()).ShouldBe(0);
        (await check.CartItems.CountAsync()).ShouldBe(2);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldSaveNothing_WhenTheCartIsEmpty()
    {
        string connectionString = postgres.NewDatabaseConnectionString();
        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            await context.Database.EnsureCreatedAsync();
            await _seed.SaveAsync(context);
        }

        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            ServiceResult<Guid> result = await NewService(context).PlaceOrderAsync(_seed.Sara.Id, NewCheckout(PaymentMethod.Cash));
            result.Error.ShouldBe(OrderErrors.EmptyCart);
        }

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await check.Orders.CountAsync()).ShouldBe(0);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldGiveEachOrderTheNextNumber()
    {
        string connectionString = await SeedWithCartAsync();
        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            (await NewService(context).PlaceOrderAsync(_seed.Sara.Id, NewCheckout(PaymentMethod.Cash))).Succeeded.ShouldBeTrue();
        }

        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            await new CartService(new CartRepository(context), new MenuRepository(context))
                .AddItemAsync(_seed.Sara.Id, new AddToCartViewModel { MenuItemId = _seed.Lemonade.Id, Quantity = 1 });
        }

        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            (await NewService(context).PlaceOrderAsync(_seed.Sara.Id, NewCheckout(PaymentMethod.Cash))).Succeeded.ShouldBeTrue();
        }

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await check.Orders.OrderBy(o => o.OrderNumber).Select(o => o.OrderNumber).ToListAsync()).ShouldBe(["FO-1000", "FO-1001"]);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task GetOrderDetailsAsync_ShouldReturnNull_WhenTheOrderBelongsToAnotherCustomer()
    {
        string connectionString = await SeedCustomersAsync();
        Guid omarsOrderId = await SeedOrderAsync(connectionString, _seed.Omar.Id, "FO-1000", OrderStatus.Placed);

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        OrderService service = NewService(context);

        (await service.GetOrderDetailsAsync(omarsOrderId, _seed.Sara.Id)).ShouldBeNull();
        (await service.CancelOrderAsync(omarsOrderId, _seed.Sara.Id)).Error.ShouldBe(OrderErrors.OrderNotFound);

        OrderDetailsViewModel? own = await service.GetOrderDetailsAsync(omarsOrderId, _seed.Omar.Id);
        own.ShouldNotBeNull();
        own.Status.ShouldBe(OrderStatus.Placed);
        OrderItemViewModel burger = own.Items.ShouldHaveSingleItem();
        burger.Extras.Select(e => e.Name).ShouldBe(["Bacon", "Extra cheese"]);
        own.StatusHistory.ShouldHaveSingleItem().Status.ShouldBe(OrderStatus.Placed);
        own.CanCancel.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCustomerOrdersAsync_ShouldReturnOnlyTheCustomersOwnOrdersNewestFirst()
    {
        string connectionString = await SeedCustomersAsync();
        await SeedOrderAsync(connectionString, _seed.Sara.Id, "FO-1000", OrderStatus.Delivered);
        await SeedOrderAsync(connectionString, _seed.Omar.Id, "FO-1001", OrderStatus.Placed);
        await SeedOrderAsync(connectionString, _seed.Sara.Id, "FO-1002", OrderStatus.Accepted);
        await SeedOrderAsync(connectionString, _seed.Sara.Id, "FO-1003", OrderStatus.Placed);

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        IReadOnlyList<OrderListItemViewModel> orders = await NewService(context).GetCustomerOrdersAsync(_seed.Sara.Id);

        orders.Select(o => o.OrderNumber).ShouldBe(["FO-1003", "FO-1002", "FO-1000"]);
        orders.Select(o => o.Status).ShouldBe([OrderStatus.Placed, OrderStatus.Accepted, OrderStatus.Delivered]);
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldSaveTheStatusTheHistoryRowAndOneOutboxRowInOneSave()
    {
        string connectionString = await SeedCustomersAsync();
        Guid orderId = await SeedOrderAsync(connectionString, _seed.Sara.Id, "FO-1000", OrderStatus.Placed);

        await using (ClientDbContext context = NewCountingContext(connectionString))
        {
            ServiceResult result = await NewService(context).CancelOrderAsync(orderId, _seed.Sara.Id);
            result.Succeeded.ShouldBeTrue();
        }

        _saves.Count.ShouldBe(1);

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        Order order = await check.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Id == orderId);
        order.Status.ShouldBe(OrderStatus.Cancelled);
        OrderStatusHistory cancelled = order.StatusHistory.Single(h => h.Status == OrderStatus.Cancelled);
        cancelled.ChangedAt.ShouldBe(order.LastStatusChangedAt);

        OutboxMessage row = await check.Set<OutboxMessage>().SingleAsync();
        row.Type.ShouldBe(nameof(OrderCancelled));
        row.CorrelationId.ShouldBe(orderId);
        OrderCancelled message = row.ToMessage().ShouldBeOfType<OrderCancelled>();
        message.OrderId.ShouldBe(orderId);
        message.Reason.ShouldBe(OrderService.CancelReason);
        message.CancelledAt.ShouldBe(order.LastStatusChangedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReportTheRestaurantUpdate_WhenTheOrderIsAcceptedBetweenLoadAndSave()
    {
        string connectionString = await SeedCustomersAsync();
        Guid orderId = await SeedOrderAsync(connectionString, _seed.Sara.Id, "FO-1000", OrderStatus.Placed);
        var acceptBeforeSave = new BeforeFirstSave(async () =>
        {
            await using ClientDbContext consumerContext = postgres.NewDbContext<ClientDbContext>(connectionString);
            var statusService = new OrderStatusService(
                new OrderStatusRepository(consumerContext),
                NullLogger<OrderStatusService>.Instance);
            await statusService.ApplyStatusChangeAsync(new OrderStatusChanged
            {
                CorrelationId = orderId,
                OrderId = orderId,
                NewStatus = OrderStatus.Accepted,
                EstimatedDeliveryTime = DateTime.UtcNow.AddMinutes(45),
                ChangedAt = DateTime.UtcNow
            });
        });

        await using (ClientDbContext context = NewContext(connectionString, acceptBeforeSave))
        {
            ServiceResult result = await NewService(context).CancelOrderAsync(orderId, _seed.Sara.Id);

            result.Error.ShouldBe(OrderErrors.ChangedByRestaurant);
            context.ChangeTracker.Entries().ShouldBeEmpty();
        }

        acceptBeforeSave.Ran.ShouldBeTrue();

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        Order order = await check.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Id == orderId);
        order.Status.ShouldBe(OrderStatus.Accepted);
        order.StatusHistory.Select(h => h.Status).OrderBy(s => s).ShouldBe([OrderStatus.Placed, OrderStatus.Accepted]);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(0);
    }

    private async Task<string> SeedCustomersAsync()
    {
        string connectionString = postgres.NewDatabaseConnectionString();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        await context.Database.EnsureCreatedAsync();
        await _seed.SaveAsync(context);
        return connectionString;
    }

    private async Task<Guid> SeedOrderAsync(string connectionString, Guid customerId, string orderNumber, OrderStatus status)
    {
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        DateTime placedAt = DateTime.UtcNow;
        Guid orderId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            DeliveryType = DeliveryType.Delivery,
            Address = "Hamra Street 12, Beirut",
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.CashOnDelivery,
            Status = status,
            Total = 24.00m,
            LastStatusChangedAt = placedAt,
            Items =
            [
                new OrderItem
                {
                    Id = itemId,
                    OrderId = orderId,
                    MenuItemId = _seed.ChickenBurger.Id,
                    Name = "Chicken Burger",
                    UnitPrice = 9.50m,
                    Quantity = 2,
                    Extras =
                    [
                        new OrderItemExtra { Id = Guid.NewGuid(), OrderItemId = itemId, Name = "Extra cheese", Price = 1.00m },
                        new OrderItemExtra { Id = Guid.NewGuid(), OrderItemId = itemId, Name = "Bacon", Price = 1.50m }
                    ]
                }
            ],
            StatusHistory =
            [
                new OrderStatusHistory { Id = Guid.NewGuid(), OrderId = orderId, Status = OrderStatus.Placed, ChangedAt = placedAt }
            ]
        });
        await context.SaveChangesAsync();
        return orderId;
    }

    private async Task<string> SeedWithCartAsync()
    {
        string connectionString = postgres.NewDatabaseConnectionString();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        await context.Database.EnsureCreatedAsync();
        await _seed.SaveAsync(context);

        var cartService = new CartService(new CartRepository(context), new MenuRepository(context));
        (await cartService.AddItemAsync(_seed.Sara.Id, new AddToCartViewModel
        {
            MenuItemId = _seed.ChickenBurger.Id,
            Quantity = 2,
            ExtraIds = [_seed.Cheese.Id, _seed.Bacon.Id]
        })).Succeeded.ShouldBeTrue();
        (await cartService.AddItemAsync(_seed.Sara.Id, new AddToCartViewModel
        {
            MenuItemId = _seed.Lemonade.Id,
            Quantity = 1
        })).Succeeded.ShouldBeTrue();

        return connectionString;
    }

    private ClientDbContext NewCountingContext(string connectionString) => NewContext(connectionString, _saves);

    private static ClientDbContext NewContext(string connectionString, IInterceptor interceptor)
    {
        DbContextOptions<ClientDbContext> options = new DbContextOptionsBuilder<ClientDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new ClientDbContext(options);
    }

    private static OrderService NewService(ClientDbContext context) =>
        new(new OrderRepository(context), new CartRepository(context), new EfOutbox<ClientDbContext>(context));

    private static CheckoutViewModel NewCheckout(PaymentMethod paymentMethod) => new()
    {
        DeliveryType = DeliveryType.Delivery,
        Address = "Hamra Street 12, Beirut",
        Notes = "Please ring twice",
        PaymentMethod = paymentMethod
    };

    private sealed class BeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Ran)
            {
                Ran = true;
                await action();
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
