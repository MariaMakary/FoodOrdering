using Client.Web.Models.Entities;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Client.Web.Services;
using NSubstitute;
using Shouldly;

namespace FoodOrdering.UnitTests.Client.Services;

public class CartServiceTests
{
    private readonly ICartRepository _carts = Substitute.For<ICartRepository>();
    private readonly IMenuRepository _menu = Substitute.For<IMenuRepository>();
    private readonly CartService _service;
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly MenuItem _burger;
    private readonly MenuItemExtra _cheese;
    private readonly MenuItemExtra _bacon;

    public CartServiceTests()
    {
        _service = new CartService(_carts, _menu);

        _burger = new MenuItem { Id = Guid.NewGuid(), Name = "Chicken Burger", Price = 9.50m, IsAvailable = true };
        _cheese = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = _burger.Id, Name = "Extra cheese", Price = 1.00m };
        _bacon = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = _burger.Id, Name = "Bacon", Price = 1.50m };
        _burger.Extras = [_cheese, _bacon];

        _menu.GetByIdAsync(_burger.Id, Arg.Any<CancellationToken>()).Returns(_burger);
    }

    [Fact]
    public async Task GetCartAsync_ShouldReturnAnEmptyCart_WhenTheCustomerHasNoCart()
    {
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns((Cart?)null);

        CartViewModel model = await _service.GetCartAsync(_customerId);

        model.IsEmpty.ShouldBeTrue();
        model.Subtotal.ShouldBe(0m);
    }

    [Fact]
    public async Task GetCartAsync_ShouldPriceEachLineWithItsExtrasFromTheMenu()
    {
        Cart cart = NewCart();
        cart.Items.Add(NewCartItem(cart, _burger, 2, _cheese, _bacon));
        MenuItem lemonade = new() { Id = Guid.NewGuid(), Name = "Lemonade", Price = 3.00m, IsAvailable = true };
        cart.Items.Add(NewCartItem(cart, lemonade, 1));
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns(cart);

        CartViewModel model = await _service.GetCartAsync(_customerId);

        model.Items.Count.ShouldBe(2);
        CartItemViewModel burgerLine = model.Items.Single(i => i.MenuItemId == _burger.Id);
        burgerLine.Name.ShouldBe("Chicken Burger");
        burgerLine.Quantity.ShouldBe(2);
        burgerLine.UnitPrice.ShouldBe(12.00m);
        burgerLine.LineTotal.ShouldBe(24.00m);
        burgerLine.Extras.Select(e => (e.Name, e.Price)).ShouldBe([("Bacon", 1.50m), ("Extra cheese", 1.00m)]);
        burgerLine.IsAvailable.ShouldBeTrue();
        model.Subtotal.ShouldBe(27.00m);
    }

    [Fact]
    public async Task GetCartAsync_ShouldMarkTheLineUnavailable_WhenTheDishIsSoldOut()
    {
        Cart cart = NewCart();
        _burger.IsAvailable = false;
        cart.Items.Add(NewCartItem(cart, _burger, 1));
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns(cart);

        CartViewModel model = await _service.GetCartAsync(_customerId);

        model.Items.ShouldHaveSingleItem().IsAvailable.ShouldBeFalse();
        model.HasUnavailableItems.ShouldBeTrue();
    }

    [Fact]
    public async Task AddItemAsync_ShouldCreateTheCartAndAddTheLine_WhenTheCustomerHasNoCart()
    {
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns((Cart?)null);
        Cart? createdCart = null;
        _carts.Add(Arg.Do<Cart>(c => createdCart = c));
        CartItem? addedItem = null;
        _carts.AddItem(Arg.Do<CartItem>(i => addedItem = i));

        ServiceResult result = await _service.AddItemAsync(_customerId, NewAdd(2, _cheese.Id));

        result.Succeeded.ShouldBeTrue();
        createdCart.ShouldNotBeNull();
        createdCart.CustomerId.ShouldBe(_customerId);
        addedItem.ShouldNotBeNull();
        addedItem.CartId.ShouldBe(createdCart.Id);
        addedItem.MenuItemId.ShouldBe(_burger.Id);
        addedItem.Quantity.ShouldBe(2);
        addedItem.Extras.Select(e => e.MenuItemExtraId).ShouldBe([_cheese.Id]);
        await _carts.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddItemAsync_ShouldIncreaseTheQuantity_WhenTheSameDishWithTheSameExtrasIsInTheCart()
    {
        Cart cart = NewCart();
        CartItem existing = NewCartItem(cart, _burger, 1, _cheese, _bacon);
        cart.Items.Add(existing);
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns(cart);

        ServiceResult result = await _service.AddItemAsync(_customerId, NewAdd(2, _bacon.Id, _cheese.Id));

        result.Succeeded.ShouldBeTrue();
        existing.Quantity.ShouldBe(3);
        _carts.DidNotReceive().AddItem(Arg.Any<CartItem>());
        _carts.DidNotReceive().Add(Arg.Any<Cart>());
        await _carts.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddItemAsync_ShouldAddANewLine_WhenTheSameDishHasDifferentExtras()
    {
        Cart cart = NewCart();
        CartItem existing = NewCartItem(cart, _burger, 1, _cheese);
        cart.Items.Add(existing);
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns(cart);

        ServiceResult result = await _service.AddItemAsync(_customerId, NewAdd(1, _cheese.Id, _bacon.Id));

        result.Succeeded.ShouldBeTrue();
        existing.Quantity.ShouldBe(1);
        _carts.Received(1).AddItem(Arg.Is<CartItem>(i => i.CartId == cart.Id && i.Extras.Count == 2));
    }

    [Fact]
    public async Task AddItemAsync_ShouldAddANewLine_WhenTheCartHasTheDishWithExtrasButTheNewOneHasNone()
    {
        Cart cart = NewCart();
        cart.Items.Add(NewCartItem(cart, _burger, 1, _cheese));
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns(cart);

        await _service.AddItemAsync(_customerId, NewAdd(1));

        _carts.Received(1).AddItem(Arg.Is<CartItem>(i => i.Extras.Count == 0));
    }

    [Fact]
    public async Task AddItemAsync_ShouldStopAtTheMaxQuantity_WhenMergingWouldGoOver()
    {
        Cart cart = NewCart();
        CartItem existing = NewCartItem(cart, _burger, CartLimits.MaxQuantity - 1);
        cart.Items.Add(existing);
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns(cart);

        await _service.AddItemAsync(_customerId, NewAdd(5));

        existing.Quantity.ShouldBe(CartLimits.MaxQuantity);
    }

    [Fact]
    public async Task AddItemAsync_ShouldFail_WhenTheDishDoesNotExistOrIsDeleted()
    {
        _menu.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MenuItem?)null);

        ServiceResult result = await _service.AddItemAsync(_customerId, NewAdd(1));

        result.Error.ShouldBe(CartErrors.DishNotFound);
        await _carts.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddItemAsync_ShouldFail_WhenTheDishIsSoldOut()
    {
        _burger.IsAvailable = false;

        ServiceResult result = await _service.AddItemAsync(_customerId, NewAdd(1));

        result.Error.ShouldBe(CartErrors.DishUnavailable);
        await _carts.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddItemAsync_ShouldFail_WhenAnExtraBelongsToAnotherDish()
    {
        ServiceResult result = await _service.AddItemAsync(_customerId, NewAdd(1, _cheese.Id, Guid.NewGuid()));

        result.Error.ShouldBe(CartErrors.InvalidExtras);
        _carts.DidNotReceive().AddItem(Arg.Any<CartItem>());
        await _carts.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CartLimits.MaxQuantity + 1)]
    public async Task AddItemAsync_ShouldFail_WhenTheQuantityIsOutOfRange(int quantity)
    {
        ServiceResult result = await _service.AddItemAsync(_customerId, NewAdd(quantity));

        result.Error.ShouldBe(CartErrors.InvalidQuantity);
        await _carts.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateQuantityAsync_ShouldSetTheNewQuantity()
    {
        Cart cart = NewCart();
        CartItem item = NewCartItem(cart, _burger, 1);
        _carts.GetItemAsync(_customerId, item.Id, Arg.Any<CancellationToken>()).Returns(item);

        ServiceResult result = await _service.UpdateQuantityAsync(_customerId, new UpdateCartItemViewModel { CartItemId = item.Id, Quantity = 4 });

        result.Succeeded.ShouldBeTrue();
        item.Quantity.ShouldBe(4);
        await _carts.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateQuantityAsync_ShouldFail_WhenTheItemIsNotInTheCustomersCart()
    {
        _carts.GetItemAsync(_customerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CartItem?)null);

        ServiceResult result = await _service.UpdateQuantityAsync(_customerId, new UpdateCartItemViewModel { CartItemId = Guid.NewGuid(), Quantity = 2 });

        result.Error.ShouldBe(CartErrors.CartItemNotFound);
        await _carts.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateQuantityAsync_ShouldFail_WhenTheQuantityIsZero()
    {
        ServiceResult result = await _service.UpdateQuantityAsync(_customerId, new UpdateCartItemViewModel { CartItemId = Guid.NewGuid(), Quantity = 0 });

        result.Error.ShouldBe(CartErrors.InvalidQuantity);
        await _carts.DidNotReceive().GetItemAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveItemAsync_ShouldRemoveTheItem()
    {
        Cart cart = NewCart();
        CartItem item = NewCartItem(cart, _burger, 1);
        _carts.GetItemAsync(_customerId, item.Id, Arg.Any<CancellationToken>()).Returns(item);

        ServiceResult result = await _service.RemoveItemAsync(_customerId, item.Id);

        result.Succeeded.ShouldBeTrue();
        _carts.Received(1).RemoveItem(item);
        await _carts.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveItemAsync_ShouldFail_WhenTheItemIsNotInTheCustomersCart()
    {
        _carts.GetItemAsync(_customerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CartItem?)null);

        ServiceResult result = await _service.RemoveItemAsync(_customerId, Guid.NewGuid());

        result.Error.ShouldBe(CartErrors.CartItemNotFound);
        _carts.DidNotReceive().RemoveItem(Arg.Any<CartItem>());
        await _carts.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearAsync_ShouldRemoveEveryItem()
    {
        Cart cart = NewCart();
        cart.Items.Add(NewCartItem(cart, _burger, 1));
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns(cart);

        await _service.ClearAsync(_customerId);

        _carts.Received(1).ClearItems(cart);
        await _carts.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearAsync_ShouldDoNothing_WhenTheCustomerHasNoCart()
    {
        _carts.GetByCustomerAsync(_customerId, Arg.Any<CancellationToken>()).Returns((Cart?)null);

        await _service.ClearAsync(_customerId);

        _carts.DidNotReceive().ClearItems(Arg.Any<Cart>());
        await _carts.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private Cart NewCart() => new() { Id = Guid.NewGuid(), CustomerId = _customerId };

    private AddToCartViewModel NewAdd(int quantity, params Guid[] extraIds) => new()
    {
        MenuItemId = _burger.Id,
        Quantity = quantity,
        ExtraIds = [.. extraIds]
    };

    private static CartItem NewCartItem(Cart cart, MenuItem dish, int quantity, params MenuItemExtra[] extras)
    {
        Guid itemId = Guid.NewGuid();
        return new CartItem
        {
            Id = itemId,
            CartId = cart.Id,
            Cart = cart,
            MenuItemId = dish.Id,
            MenuItem = dish,
            Quantity = quantity,
            Extras = extras
                .Select(e => new CartItemExtra { CartItemId = itemId, MenuItemExtraId = e.Id, MenuItemExtra = e })
                .ToList()
        };
    }
}
