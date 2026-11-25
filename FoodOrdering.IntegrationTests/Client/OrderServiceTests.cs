using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Client.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    private ClientDbContext NewCountingContext(string connectionString)
    {
        DbContextOptions<ClientDbContext> options = new DbContextOptionsBuilder<ClientDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(_saves)
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
