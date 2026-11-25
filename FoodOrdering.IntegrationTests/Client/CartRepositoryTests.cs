using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Repositories;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class CartRepositoryTests(PostgresFixture postgres)
{
    private readonly ClientMenuSeed _seed = new();

    [Fact]
    public async Task GetByCustomerAsync_ShouldLoadTheItemsWithTheirDishesAndExtras()
    {
        await using ClientDbContext context = await SeedAsync();
        Guid itemId = await AddCartItemAsync(context, _seed.Sara.Id, _seed.ChickenBurger, 2, _seed.Cheese, _seed.Bacon);

        Cart? cart = await new CartRepository(context).GetByCustomerAsync(_seed.Sara.Id);

        cart.ShouldNotBeNull();
        CartItem item = cart.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(itemId);
        item.MenuItem.Price.ShouldBe(9.50m);
        item.Extras.Select(e => e.MenuItemExtra.Name).OrderBy(n => n).ShouldBe(["Bacon", "Extra cheese"]);
    }

    [Fact]
    public async Task GetByCustomerAsync_ShouldReturnNull_WhenTheCustomerHasNoCart()
    {
        await using ClientDbContext context = await SeedAsync();

        Cart? cart = await new CartRepository(context).GetByCustomerAsync(_seed.Sara.Id);

        cart.ShouldBeNull();
    }

    [Fact]
    public async Task GetItemAsync_ShouldReturnTheItem_WhenItIsInTheCustomersCart()
    {
        await using ClientDbContext context = await SeedAsync();
        Guid itemId = await AddCartItemAsync(context, _seed.Sara.Id, _seed.Lemonade, 1);

        CartItem? item = await new CartRepository(context).GetItemAsync(_seed.Sara.Id, itemId);

        item.ShouldNotBeNull();
        item.Id.ShouldBe(itemId);
    }

    [Fact]
    public async Task GetItemAsync_ShouldReturnNull_WhenTheItemIsInAnotherCustomersCart()
    {
        await using ClientDbContext context = await SeedAsync();
        Guid omarsItemId = await AddCartItemAsync(context, _seed.Omar.Id, _seed.Lemonade, 1);

        CartItem? item = await new CartRepository(context).GetItemAsync(_seed.Sara.Id, omarsItemId);

        item.ShouldBeNull();
    }

    [Fact]
    public async Task ClearItems_ShouldDeleteTheItemsAndTheirExtrasButKeepTheCart()
    {
        await using ClientDbContext context = await SeedAsync();
        await AddCartItemAsync(context, _seed.Sara.Id, _seed.ChickenBurger, 1, _seed.Cheese);
        await AddCartItemAsync(context, _seed.Sara.Id, _seed.Lemonade, 2);
        var repository = new CartRepository(context);
        Cart cart = (await repository.GetByCustomerAsync(_seed.Sara.Id))!;

        repository.ClearItems(cart);
        await repository.SaveChangesAsync();

        context.ChangeTracker.Clear();
        (await context.Carts.CountAsync()).ShouldBe(1);
        (await context.CartItems.CountAsync()).ShouldBe(0);
        (await context.CartItemExtras.CountAsync()).ShouldBe(0);
    }

    private async Task<ClientDbContext> SeedAsync()
    {
        ClientDbContext context = await postgres.CreateDbContextAsync<ClientDbContext>();
        await _seed.SaveAsync(context);
        return context;
    }

    private static async Task<Guid> AddCartItemAsync(
        ClientDbContext context,
        Guid customerId,
        MenuItem dish,
        int quantity,
        params MenuItemExtra[] extras)
    {
        Cart? cart = await context.Carts.SingleOrDefaultAsync(c => c.CustomerId == customerId);
        if (cart is null)
        {
            cart = new Cart { Id = Guid.NewGuid(), CustomerId = customerId };
            context.Carts.Add(cart);
        }

        Guid itemId = Guid.NewGuid();
        context.CartItems.Add(new CartItem
        {
            Id = itemId,
            CartId = cart.Id,
            MenuItemId = dish.Id,
            Quantity = quantity,
            Extras = extras.Select(e => new CartItemExtra { CartItemId = itemId, MenuItemExtraId = e.Id }).ToList()
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return itemId;
    }
}
