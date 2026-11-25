using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Client.Web.Repositories;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class MenuRepositoryTests(PostgresFixture postgres)
{
    private readonly Category _burgers = new() { Id = Guid.NewGuid(), Name = "Burgers", SortOrder = 1 };
    private readonly Category _salads = new() { Id = Guid.NewGuid(), Name = "Salads", SortOrder = 2 };
    private readonly Category _drinks = new() { Id = Guid.NewGuid(), Name = "Drinks", SortOrder = 3 };

    [Fact]
    public async Task GetDishesAsync_ShouldReturnEveryDishOrderedByCategoryThenName_WhenThereAreNoFilters()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(null, null, false, null);

        dishes.Select(d => d.Name).ShouldBe(["Beef Burger", "Chicken Burger", "Caesar Salad", "Greek Salad", "Lemonade"]);
        dishes.ShouldAllBe(d => d.Category != null);
    }

    [Fact]
    public async Task GetDishesAsync_ShouldNeverReturnSoftDeletedDishes()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(null, "Old", false, null);

        dishes.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetDishesAsync_ShouldReturnSoldOutDishesAsUnavailable()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(null, null, false, null);

        dishes.Single(d => d.Name == "Beef Burger").IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task GetDishesAsync_ShouldReturnOnlyTheCategory_WhenACategoryIsSelected()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(_salads.Id, null, false, null);

        dishes.Select(d => d.Name).ShouldBe(["Caesar Salad", "Greek Salad"]);
    }

    [Theory]
    [InlineData("burger")]
    [InlineData("BURGER")]
    [InlineData(" burger ")]
    public async Task GetDishesAsync_ShouldMatchTheNameIgnoringCase_WhenSearching(string search)
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(null, search, false, null);

        dishes.Select(d => d.Name).ShouldBe(["Beef Burger", "Chicken Burger"]);
    }

    [Fact]
    public async Task GetDishesAsync_ShouldMatchTheDescription_WhenSearching()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(null, "feta", false, null);

        dishes.Select(d => d.Name).ShouldBe(["Greek Salad"]);
    }

    [Fact]
    public async Task GetDishesAsync_ShouldTreatWildcardsAsText_WhenSearching()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(null, "%", false, null);

        dishes.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetDishesAsync_ShouldReturnOnlyVegetarianDishes_WhenVegetarianIsSet()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(null, null, true, null);

        dishes.Select(d => d.Name).ShouldBe(["Greek Salad", "Lemonade"]);
    }

    [Fact]
    public async Task GetDishesAsync_ShouldIncludeTheMaxPrice_WhenAMaxPriceIsSet()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(null, null, false, 8.00m);

        dishes.Select(d => d.Name).ShouldBe(["Caesar Salad", "Greek Salad", "Lemonade"]);
    }

    [Fact]
    public async Task GetDishesAsync_ShouldApplyEveryFilter_WhenTheyAreCombined()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<MenuItem> dishes = await new MenuRepository(context).GetDishesAsync(_salads.Id, "salad", true, 7.50m);

        dishes.Select(d => d.Name).ShouldBe(["Greek Salad"]);
    }

    [Fact]
    public async Task GetCategoriesAsync_ShouldOrderCategoriesBySortOrder()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        List<Category> categories = await new MenuRepository(context).GetCategoriesAsync();

        categories.Select(c => c.Name).ShouldBe(["Burgers", "Salads", "Drinks"]);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnTheDishWithItsCategoryAndExtras()
    {
        await using ClientDbContext context = await SeedMenuAsync();
        Guid chickenBurgerId = context.MenuItems.Single(m => m.Name == "Chicken Burger").Id;
        context.ChangeTracker.Clear();

        MenuItem? dish = await new MenuRepository(context).GetByIdAsync(chickenBurgerId);

        dish.ShouldNotBeNull();
        dish.Category.Name.ShouldBe("Burgers");
        dish.Extras.Select(e => (e.Name, e.Price)).ShouldBe([("Bacon", 1.50m), ("Extra cheese", 1.00m)]);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenTheDishDoesNotExist()
    {
        await using ClientDbContext context = await SeedMenuAsync();

        MenuItem? dish = await new MenuRepository(context).GetByIdAsync(Guid.NewGuid());

        dish.ShouldBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenTheDishIsSoftDeleted()
    {
        await using ClientDbContext context = await SeedMenuAsync();
        Guid deletedId = context.MenuItems.Single(m => m.Name == "Old Burger").Id;
        context.ChangeTracker.Clear();

        MenuItem? dish = await new MenuRepository(context).GetByIdAsync(deletedId);

        dish.ShouldBeNull();
    }

    [Fact]
    public async Task GetFeaturedAsync_ShouldReturnOnlyAvailableDishesNewestFirst_WhenThereAreNoRatings()
    {
        await using ClientDbContext context = await SeedMenuAsync();
        await SetCreatedAtAsync(context, "Lemonade", 1);
        await SetCreatedAtAsync(context, "Caesar Salad", 2);
        await SetCreatedAtAsync(context, "Greek Salad", 3);
        await SetCreatedAtAsync(context, "Chicken Burger", 4);

        List<MenuItem> dishes = await new MenuRepository(context).GetFeaturedAsync(6);

        dishes.Select(d => d.Name).ShouldBe(["Chicken Burger", "Greek Salad", "Caesar Salad", "Lemonade"]);
        dishes.ShouldAllBe(d => d.Category != null);
    }

    [Fact]
    public async Task GetFeaturedAsync_ShouldReturnAtMostTheCount_WhenThereAreMoreDishes()
    {
        await using ClientDbContext context = await postgres.CreateDbContextAsync<ClientDbContext>();
        context.Categories.Add(_burgers);
        for (int i = 1; i <= 8; i++)
        {
            context.MenuItems.Add(NewDish(_burgers, $"Burger {i}", 9.00m));
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        List<MenuItem> dishes = await new MenuRepository(context).GetFeaturedAsync(6);

        dishes.Count.ShouldBe(6);
        dishes.ShouldAllBe(d => d.IsAvailable && !d.IsDeleted);
    }

    [Fact]
    public async Task GetFeaturedAsync_ShouldPutTheTopRatedFirstAndUnratedAfter_WhenThereAreRatings()
    {
        await using ClientDbContext context = await SeedMenuAsync();
        await SetCreatedAtAsync(context, "Lemonade", 1);
        await SetCreatedAtAsync(context, "Caesar Salad", 2);
        await SetCreatedAtAsync(context, "Greek Salad", 3);
        await SetCreatedAtAsync(context, "Chicken Burger", 4);
        ApplicationUser customer = await AddCustomerAsync(context);
        await AddRatingAsync(context, customer, "Lemonade", 5);
        await AddRatingAsync(context, customer, "Lemonade", 4);
        await AddRatingAsync(context, customer, "Caesar Salad", 3);

        List<MenuItem> dishes = await new MenuRepository(context).GetFeaturedAsync(6);

        dishes.Select(d => d.Name).ShouldBe(["Lemonade", "Caesar Salad", "Chicken Burger", "Greek Salad"]);
    }

    [Fact]
    public async Task GetFeaturedAsync_ShouldPutTheDishWithMoreRatingsFirst_WhenTheAveragesAreEqual()
    {
        await using ClientDbContext context = await SeedMenuAsync();
        await SetCreatedAtAsync(context, "Lemonade", 1);
        await SetCreatedAtAsync(context, "Greek Salad", 2);
        ApplicationUser customer = await AddCustomerAsync(context);
        await AddRatingAsync(context, customer, "Greek Salad", 4);
        await AddRatingAsync(context, customer, "Lemonade", 4);
        await AddRatingAsync(context, customer, "Lemonade", 4);

        List<MenuItem> dishes = await new MenuRepository(context).GetFeaturedAsync(2);

        dishes.Select(d => d.Name).ShouldBe(["Lemonade", "Greek Salad"]);
    }

    [Fact]
    public async Task GetFeaturedAsync_ShouldLeaveOutUnavailableAndDeletedDishes_WhenTheyAreTopRated()
    {
        await using ClientDbContext context = await SeedMenuAsync();
        ApplicationUser customer = await AddCustomerAsync(context);
        await AddRatingAsync(context, customer, "Beef Burger", 5);
        await AddRatingAsync(context, customer, "Old Burger", 5);
        await AddRatingAsync(context, customer, "Lemonade", 1);

        List<MenuItem> dishes = await new MenuRepository(context).GetFeaturedAsync(6);

        dishes.Select(d => d.Name).ShouldNotContain("Beef Burger");
        dishes.Select(d => d.Name).ShouldNotContain("Old Burger");
        dishes.First().Name.ShouldBe("Lemonade");
        dishes.Count.ShouldBe(4);
    }

    private static async Task SetCreatedAtAsync(ClientDbContext context, string dishName, int day)
    {
        var createdAt = new DateTime(2026, 1, day, 12, 0, 0, DateTimeKind.Utc);
        await context.MenuItems
            .Where(m => m.Name == dishName)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, createdAt));
    }

    private static async Task<ApplicationUser> AddCustomerAsync(ClientDbContext context)
    {
        var customer = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "sara@example.com",
            Email = "sara@example.com",
            FirstName = "Sara",
            LastName = "Haddad"
        };
        context.Users.Add(customer);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return customer;
    }

    private static async Task AddRatingAsync(ClientDbContext context, ApplicationUser customer, string dishName, int stars)
    {
        Guid dishId = await context.MenuItems.Where(m => m.Name == dishName).Select(m => m.Id).SingleAsync();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"FO-{Guid.NewGuid():N}"[..20],
            CustomerId = customer.Id,
            DeliveryType = DeliveryType.Pickup,
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.CashOnDelivery,
            Status = OrderStatus.Delivered,
            Total = 10.00m,
            LastStatusChangedAt = DateTime.UtcNow
        };
        context.Orders.Add(order);
        context.Ratings.Add(new Rating
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            MenuItemId = dishId,
            CustomerId = customer.Id,
            Stars = stars
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private async Task<ClientDbContext> SeedMenuAsync()
    {
        ClientDbContext context = await postgres.CreateDbContextAsync<ClientDbContext>();
        context.Categories.AddRange(_drinks, _salads, _burgers);

        MenuItem chickenBurger = NewDish(_burgers, "Chicken Burger", 9.50m);
        chickenBurger.Extras =
        [
            new MenuItemExtra { Id = Guid.NewGuid(), Name = "Extra cheese", Price = 1.00m },
            new MenuItemExtra { Id = Guid.NewGuid(), Name = "Bacon", Price = 1.50m }
        ];

        MenuItem beefBurger = NewDish(_burgers, "Beef Burger", 11.00m);
        beefBurger.IsAvailable = false;

        MenuItem oldBurger = NewDish(_burgers, "Old Burger", 5.00m);
        oldBurger.IsDeleted = true;

        MenuItem caesarSalad = NewDish(_salads, "Caesar Salad", 8.00m);
        caesarSalad.Description = "Romaine, chicken and parmesan";

        MenuItem greekSalad = NewDish(_salads, "Greek Salad", 7.00m);
        greekSalad.Description = "Tomato, cucumber and feta";
        greekSalad.IsVegetarian = true;

        MenuItem lemonade = NewDish(_drinks, "Lemonade", 3.00m);
        lemonade.IsVegetarian = true;

        context.MenuItems.AddRange(lemonade, greekSalad, caesarSalad, oldBurger, beefBurger, chickenBurger);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return context;
    }

    private static MenuItem NewDish(Category category, string name, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        CategoryId = category.Id,
        Name = name,
        Price = price,
        SourceUpdatedAt = DateTime.UtcNow
    };
}
