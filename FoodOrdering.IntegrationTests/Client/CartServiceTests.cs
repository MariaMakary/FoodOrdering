using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Client.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class CartServiceTests(PostgresFixture postgres)
{
    private readonly ClientMenuSeed _seed = new();

    [Fact]
    public async Task AddItemAsync_ShouldIncreaseTheQuantity_WhenTheSameDishWithTheSameExtrasIsAddedTwice()
    {
        await using ClientDbContext context = await SeedAsync();

        (await NewService(context).AddItemAsync(_seed.Sara.Id, NewAdd(_seed.ChickenBurger, 1, _seed.Cheese, _seed.Bacon))).Succeeded.ShouldBeTrue();
        context.ChangeTracker.Clear();
        (await NewService(context).AddItemAsync(_seed.Sara.Id, NewAdd(_seed.ChickenBurger, 2, _seed.Bacon, _seed.Cheese))).Succeeded.ShouldBeTrue();

        context.ChangeTracker.Clear();
        CartItem item = await context.CartItems.Include(i => i.Extras).SingleAsync();
        item.Quantity.ShouldBe(3);
        item.Extras.Count.ShouldBe(2);
        (await context.Carts.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task AddItemAsync_ShouldAddASecondLine_WhenTheExtrasAreDifferent()
    {
        await using ClientDbContext context = await SeedAsync();

        await NewService(context).AddItemAsync(_seed.Sara.Id, NewAdd(_seed.ChickenBurger, 1, _seed.Cheese));
        context.ChangeTracker.Clear();
        await NewService(context).AddItemAsync(_seed.Sara.Id, NewAdd(_seed.ChickenBurger, 1));

        context.ChangeTracker.Clear();
        (await context.CartItems.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task AddItemAsync_ShouldRefuseAnExtraOfAnotherDish()
    {
        await using ClientDbContext context = await SeedAsync();

        ServiceResult result = await NewService(context).AddItemAsync(_seed.Sara.Id, NewAdd(_seed.ChickenBurger, 1, _seed.Pickles));

        result.Error.ShouldBe(CartErrors.InvalidExtras);
        context.ChangeTracker.Clear();
        (await context.CartItems.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AddItemAsync_ShouldRefuseADeletedDish()
    {
        await using ClientDbContext context = await SeedAsync();

        ServiceResult result = await NewService(context).AddItemAsync(_seed.Sara.Id, NewAdd(_seed.OldBurger, 1));

        result.Error.ShouldBe(CartErrors.DishNotFound);
    }

    [Fact]
    public async Task UpdateQuantityAsync_ShouldNotChangeAnotherCustomersItem()
    {
        await using ClientDbContext context = await SeedAsync();
        await NewService(context).AddItemAsync(_seed.Omar.Id, NewAdd(_seed.Lemonade, 1));
        context.ChangeTracker.Clear();
        Guid omarsItemId = (await context.CartItems.SingleAsync()).Id;
        context.ChangeTracker.Clear();

        ServiceResult result = await NewService(context).UpdateQuantityAsync(
            _seed.Sara.Id, new UpdateCartItemViewModel { CartItemId = omarsItemId, Quantity = 50 });

        result.Error.ShouldBe(CartErrors.CartItemNotFound);
        context.ChangeTracker.Clear();
        (await context.CartItems.SingleAsync()).Quantity.ShouldBe(1);
    }

    [Fact]
    public async Task RemoveItemAsync_ShouldNotRemoveAnotherCustomersItem()
    {
        await using ClientDbContext context = await SeedAsync();
        await NewService(context).AddItemAsync(_seed.Omar.Id, NewAdd(_seed.Lemonade, 1));
        context.ChangeTracker.Clear();
        Guid omarsItemId = (await context.CartItems.SingleAsync()).Id;
        context.ChangeTracker.Clear();

        ServiceResult result = await NewService(context).RemoveItemAsync(_seed.Sara.Id, omarsItemId);

        result.Error.ShouldBe(CartErrors.CartItemNotFound);
        context.ChangeTracker.Clear();
        (await context.CartItems.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task GetCartAsync_ShouldReadTheCurrentPricesFromTheMenu()
    {
        await using ClientDbContext context = await SeedAsync();
        await NewService(context).AddItemAsync(_seed.Sara.Id, NewAdd(_seed.ChickenBurger, 2, _seed.Cheese));
        await context.MenuItems.Where(m => m.Id == _seed.ChickenBurger.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.Price, 10.00m));
        context.ChangeTracker.Clear();

        CartViewModel cart = await NewService(context).GetCartAsync(_seed.Sara.Id);

        CartItemViewModel line = cart.Items.ShouldHaveSingleItem();
        line.UnitPrice.ShouldBe(11.00m);
        line.LineTotal.ShouldBe(22.00m);
        cart.Subtotal.ShouldBe(22.00m);
    }

    private async Task<ClientDbContext> SeedAsync()
    {
        ClientDbContext context = await postgres.CreateDbContextAsync<ClientDbContext>();
        await _seed.SaveAsync(context);
        return context;
    }

    private static CartService NewService(ClientDbContext context) =>
        new(new CartRepository(context), new MenuRepository(context));

    private static AddToCartViewModel NewAdd(MenuItem dish, int quantity, params MenuItemExtra[] extras) => new()
    {
        MenuItemId = dish.Id,
        Quantity = quantity,
        ExtraIds = extras.Select(e => e.Id).ToList()
    };
}
