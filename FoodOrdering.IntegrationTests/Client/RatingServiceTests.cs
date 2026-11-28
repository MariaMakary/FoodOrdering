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
public class RatingServiceTests(PostgresFixture postgres)
{
    private readonly ClientMenuSeed _seed = new();
    private readonly SaveCounter _saves = new();
    private int _orderCount;

    [Fact]
    public async Task SubmitAsync_ShouldSaveTheRatingsAndOneOutboxRowPerDishInOneSave()
    {
        string connectionString = await SeedCustomersAsync();
        Guid orderId = await SeedOrderAsync(connectionString, _seed.Sara.Id, OrderStatus.Delivered);

        await using (ClientDbContext context = NewContext(connectionString, _saves))
        {
            ServiceResult result = await NewService(context).SubmitAsync(_seed.Sara.Id, NewForm(orderId,
                (_seed.ChickenBurger.Id, 5, "Best burger in town!"),
                (_seed.Lemonade.Id, 3, null)));
            result.Succeeded.ShouldBeTrue();
        }

        _saves.Count.ShouldBe(1);

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        List<Rating> ratings = await check.Ratings.ToListAsync();
        ratings.Count.ShouldBe(2);
        ratings.ShouldAllBe(r => r.OrderId == orderId && r.CustomerId == _seed.Sara.Id);
        Rating burger = ratings.Single(r => r.MenuItemId == _seed.ChickenBurger.Id);
        burger.Stars.ShouldBe(5);
        burger.Comment.ShouldBe("Best burger in town!");
        burger.CreatedAt.ShouldNotBe(default);

        List<OutboxMessage> rows = await check.Set<OutboxMessage>().ToListAsync();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.Type == nameof(DishRated) && r.CorrelationId == orderId);
        List<DishRated> messages = rows.Select(r => r.ToMessage().ShouldBeOfType<DishRated>()).ToList();
        messages.Select(m => m.RatingId).ShouldBe(ratings.Select(r => r.Id), ignoreOrder: true);
        DishRated burgerMessage = messages.Single(m => m.MenuItemId == _seed.ChickenBurger.Id);
        burgerMessage.RatingId.ShouldBe(burger.Id);
        burgerMessage.OrderId.ShouldBe(orderId);
        burgerMessage.CustomerName.ShouldBe("Sara Haddad");
        burgerMessage.Stars.ShouldBe(5);
        burgerMessage.Comment.ShouldBe("Best burger in town!");
        burgerMessage.RatedAt.ShouldBe(burger.CreatedAt, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SubmitAsync_ShouldRefuseAndSaveNothing_WhenTheSameDishIsRatedAtTheSameTimeInAnotherRequest()
    {
        string connectionString = await SeedCustomersAsync();
        Guid orderId = await SeedOrderAsync(connectionString, _seed.Sara.Id, OrderStatus.Delivered);
        var rateInAnotherTab = new BeforeFirstSave(async () =>
        {
            await using ClientDbContext otherTab = postgres.NewDbContext<ClientDbContext>(connectionString);
            (await NewService(otherTab).SubmitAsync(_seed.Sara.Id, NewForm(orderId, (_seed.ChickenBurger.Id, 5, null))))
                .Succeeded.ShouldBeTrue();
        });

        await using (ClientDbContext context = NewContext(connectionString, rateInAnotherTab))
        {
            ServiceResult result = await NewService(context).SubmitAsync(_seed.Sara.Id, NewForm(orderId,
                (_seed.ChickenBurger.Id, 1, "Second try"),
                (_seed.Lemonade.Id, 2, null)));

            result.Error.ShouldBe(RatingErrors.AlreadyRated);
            context.ChangeTracker.Entries().ShouldBeEmpty();
        }

        rateInAnotherTab.Ran.ShouldBeTrue();

        await using ClientDbContext check = postgres.NewDbContext<ClientDbContext>(connectionString);
        Rating rating = await check.Ratings.SingleAsync();
        rating.MenuItemId.ShouldBe(_seed.ChickenBurger.Id);
        rating.Stars.ShouldBe(5);
        (await check.Set<OutboxMessage>().SingleAsync()).ToMessage().ShouldBeOfType<DishRated>().RatingId.ShouldBe(rating.Id);
    }

    [Fact]
    public async Task GetRateFormAsync_ShouldOnlyAllowTheOwnerOfADeliveredOrder()
    {
        string connectionString = await SeedCustomersAsync();
        Guid delivered = await SeedOrderAsync(connectionString, _seed.Sara.Id, OrderStatus.Delivered);
        Guid preparing = await SeedOrderAsync(connectionString, _seed.Sara.Id, OrderStatus.Preparing);

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        RatingService service = NewService(context);

        (await service.GetRateFormAsync(delivered, _seed.Omar.Id)).Error.ShouldBe(RatingErrors.OrderNotFound);
        (await service.GetRateFormAsync(preparing, _seed.Sara.Id)).Error.ShouldBe(RatingErrors.OrderNotFound);
        (await service.SubmitAsync(_seed.Omar.Id, NewForm(delivered, (_seed.ChickenBurger.Id, 5, null))))
            .Error.ShouldBe(RatingErrors.OrderNotFound);

        ServiceResult<RateOrderViewModel> own = await service.GetRateFormAsync(delivered, _seed.Sara.Id);
        own.Succeeded.ShouldBeTrue();
        own.Value!.Dishes.Select(d => d.Name).ShouldBe(["Chicken Burger", "Lemonade"], ignoreOrder: true);
        context.ChangeTracker.Entries().ShouldBeEmpty();
        (await context.Ratings.CountAsync()).ShouldBe(0);
    }

    private async Task<string> SeedCustomersAsync()
    {
        string connectionString = postgres.NewDatabaseConnectionString();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        await context.Database.EnsureCreatedAsync();
        await _seed.SaveAsync(context);
        return connectionString;
    }

    private async Task<Guid> SeedOrderAsync(string connectionString, Guid customerId, OrderStatus status)
    {
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        DateTime placedAt = DateTime.UtcNow;
        Guid orderId = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = $"FO-{1000 + _orderCount++}",
            CustomerId = customerId,
            DeliveryType = DeliveryType.Pickup,
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Paid,
            Status = status,
            Total = 22.00m,
            LastStatusChangedAt = placedAt,
            Items =
            [
                NewItem(orderId, _seed.ChickenBurger, 2),
                NewItem(orderId, _seed.Lemonade, 1)
            ]
        });
        await context.SaveChangesAsync();
        return orderId;
    }

    private static OrderItem NewItem(Guid orderId, MenuItem dish, int quantity) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        MenuItemId = dish.Id,
        Name = dish.Name,
        UnitPrice = dish.Price,
        Quantity = quantity
    };

    private static RateOrderViewModel NewForm(Guid orderId, params (Guid MenuItemId, int Stars, string? Comment)[] dishes) => new()
    {
        OrderId = orderId,
        Dishes = dishes
            .Select(d => new RateDishViewModel { MenuItemId = d.MenuItemId, Stars = d.Stars, Comment = d.Comment })
            .ToList()
    };

    private static ClientDbContext NewContext(string connectionString, IInterceptor interceptor)
    {
        DbContextOptions<ClientDbContext> options = new DbContextOptionsBuilder<ClientDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new ClientDbContext(options);
    }

    private static RatingService NewService(ClientDbContext context) =>
        new(new RatingRepository(context), new EfOutbox<ClientDbContext>(context));

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
