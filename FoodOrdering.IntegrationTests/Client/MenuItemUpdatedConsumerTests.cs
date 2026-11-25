using Client.Web;
using Client.Web.Data;
using Client.Web.Messaging.Consumers;
using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Messaging;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class MenuItemUpdatedConsumerTests(PostgresFixture postgres)
{
    private static readonly DateTime CreatedAt = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid DishId = Guid.NewGuid();
    private static readonly CategoryDto Burgers = new() { Id = Guid.NewGuid(), Name = "Burgers", SortOrder = 1 };
    private static readonly MenuItemExtraDto Cheese = new() { Id = Guid.NewGuid(), Name = "Extra cheese", Price = 1.00m };
    private static readonly MenuItemExtraDto Bacon = new() { Id = Guid.NewGuid(), Name = "Bacon", Price = 1.50m };

    [Fact]
    public async Task HandleAsync_ShouldInsertTheDishWithItsCategoryAndExtras_WhenItIsCreated()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        MenuItemUpdated message = Snapshot(MenuItemAction.Created, CreatedAt);

        await HandleAsync(connectionString, message);

        MenuItem dish = await LoadDishAsync(connectionString);
        dish.Name.ShouldBe("Chicken Burger");
        dish.Description.ShouldBe("Grilled chicken breast");
        dish.Price.ShouldBe(9.50m);
        dish.PhotoUrl.ShouldBe("https://localhost:7101/images/menu/chicken-burger.jpg");
        dish.IsVegetarian.ShouldBeFalse();
        dish.IsAvailable.ShouldBeTrue();
        dish.IsDeleted.ShouldBeFalse();
        dish.SourceUpdatedAt.ShouldBe(CreatedAt);
        dish.CategoryId.ShouldBe(Burgers.Id);
        dish.Category.Name.ShouldBe("Burgers");
        dish.Category.SortOrder.ShouldBe(1);
        dish.Extras.Select(e => (e.Id, e.Name, e.Price)).ShouldBe(
            [(Cheese.Id, Cheese.Name, Cheese.Price), (Bacon.Id, Bacon.Name, Bacon.Price)],
            ignoreOrder: true);
    }

    [Fact]
    public async Task HandleAsync_ShouldChangeThePriceAndExtras_WhenTheDishIsUpdated()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await HandleAsync(connectionString, Snapshot(MenuItemAction.Created, CreatedAt));
        MenuItemExtraDto pickles = new() { Id = Guid.NewGuid(), Name = "Pickles", Price = 0.50m };
        MenuItemUpdated updated = Snapshot(MenuItemAction.Updated, CreatedAt.AddHours(1)) with
        {
            Price = 10.25m,
            IsAvailable = false,
            Extras = [Cheese with { Price = 1.25m }, pickles]
        };

        await HandleAsync(connectionString, updated);

        MenuItem dish = await LoadDishAsync(connectionString);
        dish.Price.ShouldBe(10.25m);
        dish.IsAvailable.ShouldBeFalse();
        dish.SourceUpdatedAt.ShouldBe(updated.UpdatedAt);
        dish.Extras.Select(e => (e.Id, e.Name, e.Price)).ShouldBe(
            [(Cheese.Id, Cheese.Name, 1.25m), (pickles.Id, pickles.Name, pickles.Price)],
            ignoreOrder: true);
    }

    [Fact]
    public async Task HandleAsync_ShouldMarkTheDishUnavailableAndDeletedWithoutRemovingIt_WhenItIsDeleted()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await HandleAsync(connectionString, Snapshot(MenuItemAction.Created, CreatedAt));

        await HandleAsync(connectionString, Deleted(CreatedAt.AddHours(1)));

        MenuItem dish = await LoadDishAsync(connectionString);
        dish.IsAvailable.ShouldBeFalse();
        dish.IsDeleted.ShouldBeTrue();
        dish.Name.ShouldBe("Chicken Burger");
        dish.Price.ShouldBe(9.50m);
        dish.SourceUpdatedAt.ShouldBe(CreatedAt.AddHours(1));
        dish.Extras.Count.ShouldBe(2);
    }

    [Fact]
    public async Task HandleAsync_ShouldChangeNothing_WhenTheMessageIsOlderThanTheStoredSourceUpdatedAt()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        MenuItemUpdated newer = Snapshot(MenuItemAction.Updated, CreatedAt.AddHours(2)) with { Price = 12.00m };
        await HandleAsync(connectionString, newer);
        MenuItemUpdated older = Snapshot(MenuItemAction.Updated, CreatedAt.AddHours(1)) with
        {
            Price = 8.00m,
            Category = Burgers with { Name = "Old Burgers", SortOrder = 9 },
            Extras = []
        };

        await HandleAsync(connectionString, older);

        MenuItem dish = await LoadDishAsync(connectionString);
        dish.Price.ShouldBe(12.00m);
        dish.SourceUpdatedAt.ShouldBe(newer.UpdatedAt);
        dish.Category.Name.ShouldBe("Burgers");
        dish.Category.SortOrder.ShouldBe(1);
        dish.Extras.Count.ShouldBe(2);
    }

    [Fact]
    public async Task HandleAsync_ShouldUpdateTheCategory_WhenItHasANewSortOrder()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await HandleAsync(connectionString, Snapshot(MenuItemAction.Created, CreatedAt));

        await HandleAsync(connectionString, Snapshot(MenuItemAction.Updated, CreatedAt.AddHours(1)) with
        {
            Category = Burgers with { Name = "Grill", SortOrder = 4 }
        });

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        Category category = await context.Categories.SingleAsync();
        category.Id.ShouldBe(Burgers.Id);
        category.Name.ShouldBe("Grill");
        category.SortOrder.ShouldBe(4);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotThrowAndKeepTheCartLine_WhenAnExtraInACartIsRemoved()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await HandleAsync(connectionString, Snapshot(MenuItemAction.Created, CreatedAt));
        Guid customerId = await SeedCartLineAsync(connectionString, Cheese.Id, Bacon.Id);

        await Should.NotThrowAsync(() => HandleAsync(connectionString, Snapshot(MenuItemAction.Updated, CreatedAt.AddHours(1)) with
        {
            Extras = [Cheese]
        }));

        using IHost host = BuildHost(connectionString);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        CartViewModel cart = await scope.ServiceProvider.GetRequiredService<ICartService>().GetCartAsync(customerId);
        CartItemViewModel line = cart.Items.ShouldHaveSingleItem();
        line.MenuItemId.ShouldBe(DishId);
        line.Extras.ShouldHaveSingleItem().Name.ShouldBe(Cheese.Name);
        line.UnitPrice.ShouldBe(9.50m + 1.00m);
        line.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldChangeNothing_WhenTheSameMessageArrivesTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        MenuItemUpdated message = Snapshot(MenuItemAction.Created, CreatedAt.AddTicks(1234567));
        await HandleAsync(connectionString, message);
        MenuItem first = await LoadDishAsync(connectionString);

        await HandleAsync(connectionString, message);

        MenuItem second = await LoadDishAsync(connectionString);
        second.UpdatedAt.ShouldBe(first.UpdatedAt);
        second.SourceUpdatedAt.ShouldBe(first.SourceUpdatedAt);
        second.Extras.Count.ShouldBe(2);
    }

    [Fact]
    public async Task HandleAsync_ShouldTreatItAsAnUpdate_WhenACreatedArrivesForAnExistingDish()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await HandleAsync(connectionString, Snapshot(MenuItemAction.Created, CreatedAt));

        await HandleAsync(connectionString, Snapshot(MenuItemAction.Created, CreatedAt.AddHours(1)) with { Name = "Crispy Chicken Burger" });

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        MenuItem dish = await context.MenuItems.SingleAsync();
        dish.Name.ShouldBe("Crispy Chicken Burger");
    }

    private static MenuItemUpdated Snapshot(MenuItemAction action, DateTime updatedAt) => new()
    {
        Action = action,
        MenuItemId = DishId,
        Name = "Chicken Burger",
        Description = "Grilled chicken breast",
        Price = 9.50m,
        PhotoUrl = "https://localhost:7101/images/menu/chicken-burger.jpg",
        IsVegetarian = false,
        IsAvailable = true,
        Category = Burgers,
        Extras = [Cheese, Bacon],
        UpdatedAt = updatedAt
    };

    private static MenuItemUpdated Deleted(DateTime updatedAt) => new()
    {
        Action = MenuItemAction.Deleted,
        MenuItemId = DishId,
        IsAvailable = false,
        UpdatedAt = updatedAt
    };

    private static async Task HandleAsync(string connectionString, MenuItemUpdated message)
    {
        using IHost host = BuildHost(connectionString);
        MenuItemUpdatedConsumer consumer = host.Services.GetServices<IHostedService>().OfType<MenuItemUpdatedConsumer>().Single();

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        await consumer.HandleAsync(message, scope.ServiceProvider, CancellationToken.None);
    }

    private static IHost BuildHost(string connectionString)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "localhost",
            ["RabbitMq:Username"] = "unused",
            ["RabbitMq:Password"] = "unused",
            ["RabbitMq:ExchangeName"] = "food.events"
        });
        builder.Services.AddRabbitMq(builder.Configuration);
        builder.Services.AddDbContext<ClientDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddClientServices();
        return builder.Build();
    }

    private async Task<MenuItem> LoadDishAsync(string connectionString)
    {
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        return await context.MenuItems
            .Include(m => m.Category)
            .Include(m => m.Extras)
            .SingleAsync(m => m.Id == DishId);
    }

    private async Task<Guid> SeedCartLineAsync(string connectionString, params Guid[] extraIds)
    {
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        var customer = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "sara@example.com",
            Email = "sara@example.com",
            FirstName = "Sara",
            LastName = "Haddad"
        };
        var cart = new Cart { Id = Guid.NewGuid(), CustomerId = customer.Id };
        var line = new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, MenuItemId = DishId, Quantity = 2 };
        line.Extras = extraIds.Select(id => new CartItemExtra { CartItemId = line.Id, MenuItemExtraId = id }).ToList();
        context.Users.Add(customer);
        context.Carts.Add(cart);
        context.CartItems.Add(line);
        await context.SaveChangesAsync();
        return customer.Id;
    }
}
