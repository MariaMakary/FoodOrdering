using Client.Web.Models.Entities;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Client.Web.Services;
using NSubstitute;
using Shouldly;

namespace FoodOrdering.UnitTests.Client.Services;

public class MenuServiceTests
{
    private readonly IMenuRepository _menu = Substitute.For<IMenuRepository>();
    private readonly MenuService _service;
    private readonly Category _burgers = new() { Id = Guid.NewGuid(), Name = "Burgers", SortOrder = 1 };
    private readonly Category _salads = new() { Id = Guid.NewGuid(), Name = "Salads", SortOrder = 2 };

    public MenuServiceTests()
    {
        _service = new MenuService(_menu);
        _menu.GetCategoriesAsync(Arg.Any<CancellationToken>()).Returns([_burgers, _salads]);
        _menu.GetDishesAsync(default, default, default, default, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs([]);
    }

    [Fact]
    public async Task GetMenuAsync_ShouldMapCategoriesInTheRepositoryOrder()
    {
        MenuIndexViewModel model = await _service.GetMenuAsync(null, null, false, null);

        model.Categories.Select(c => (c.Id, c.Name)).ShouldBe([(_burgers.Id, "Burgers"), (_salads.Id, "Salads")]);
    }

    [Fact]
    public async Task GetMenuAsync_ShouldMapEveryDishToACard()
    {
        MenuItem dish = NewDish(_burgers, "Chicken Burger", 9.50m, isVegetarian: false, isAvailable: false);
        dish.PhotoUrl = "/images/chicken-burger.jpg";
        _menu.GetDishesAsync(default, default, default, default, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs([dish]);

        MenuIndexViewModel model = await _service.GetMenuAsync(null, null, false, null);

        DishCardViewModel card = model.Dishes.ShouldHaveSingleItem();
        card.Id.ShouldBe(dish.Id);
        card.Name.ShouldBe("Chicken Burger");
        card.Price.ShouldBe(9.50m);
        card.PhotoUrl.ShouldBe("/images/chicken-burger.jpg");
        card.IsVegetarian.ShouldBeFalse();
        card.IsAvailable.ShouldBeFalse();
        card.CategoryName.ShouldBe("Burgers");
    }

    [Fact]
    public async Task GetMenuAsync_ShouldPassTheFiltersToTheRepositoryAndKeepThemOnTheModel()
    {
        MenuIndexViewModel model = await _service.GetMenuAsync(_salads.Id, "  caesar ", true, 12.00m);

        await _menu.Received(1).GetDishesAsync(_salads.Id, "caesar", true, 12.00m, Arg.Any<CancellationToken>());
        model.SelectedCategoryId.ShouldBe(_salads.Id);
        model.Search.ShouldBe("caesar");
        model.Vegetarian.ShouldBeTrue();
        model.MaxPrice.ShouldBe(12.00m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetMenuAsync_ShouldNotSearch_WhenTheSearchIsBlank(string? search)
    {
        MenuIndexViewModel model = await _service.GetMenuAsync(null, search, false, null);

        await _menu.Received(1).GetDishesAsync(null, null, false, null, Arg.Any<CancellationToken>());
        model.Search.ShouldBeNull();
    }

    [Fact]
    public async Task GetMenuAsync_ShouldCutTheSearchToTheMaxLength_WhenItIsTooLong()
    {
        string longSearch = new('a', 5000);

        MenuIndexViewModel model = await _service.GetMenuAsync(null, longSearch, false, null);

        string expected = new('a', MenuIndexViewModel.SearchMaxLength);
        await _menu.Received(1).GetDishesAsync(null, expected, false, null, Arg.Any<CancellationToken>());
        model.Search.ShouldBe(expected);
    }

    [Fact]
    public async Task GetMenuAsync_ShouldIgnoreTheMaxPrice_WhenItIsNegative()
    {
        MenuIndexViewModel model = await _service.GetMenuAsync(null, null, false, -5.00m);

        await _menu.Received(1).GetDishesAsync(null, null, false, null, Arg.Any<CancellationToken>());
        model.MaxPrice.ShouldBeNull();
    }

    [Fact]
    public async Task GetDishDetailsAsync_ShouldMapTheDishWithItsExtras()
    {
        MenuItem dish = NewDish(_burgers, "Chicken Burger", 9.50m, isVegetarian: false, isAvailable: true);
        dish.Description = "Crispy chicken with lettuce";
        dish.PhotoUrl = "/images/chicken-burger.jpg";
        var cheese = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = dish.Id, Name = "Extra cheese", Price = 1.00m };
        var bacon = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = dish.Id, Name = "Bacon", Price = 1.50m };
        dish.Extras = [bacon, cheese];
        _menu.GetByIdAsync(dish.Id, Arg.Any<CancellationToken>()).Returns(dish);

        MenuItemDetailsViewModel? model = await _service.GetDishDetailsAsync(dish.Id);

        model.ShouldNotBeNull();
        model.Id.ShouldBe(dish.Id);
        model.Name.ShouldBe("Chicken Burger");
        model.Description.ShouldBe("Crispy chicken with lettuce");
        model.Price.ShouldBe(9.50m);
        model.PhotoUrl.ShouldBe("/images/chicken-burger.jpg");
        model.IsVegetarian.ShouldBeFalse();
        model.IsAvailable.ShouldBeTrue();
        model.CategoryId.ShouldBe(_burgers.Id);
        model.CategoryName.ShouldBe("Burgers");
        model.Extras.Select(e => (e.Id, e.Name, e.Price)).ShouldBe([(bacon.Id, "Bacon", 1.50m), (cheese.Id, "Extra cheese", 1.00m)]);
    }

    [Fact]
    public async Task GetDishDetailsAsync_ShouldKeepTheDishUnavailable_WhenItIsSoldOut()
    {
        MenuItem dish = NewDish(_salads, "Greek Salad", 7.00m, isVegetarian: true, isAvailable: false);
        _menu.GetByIdAsync(dish.Id, Arg.Any<CancellationToken>()).Returns(dish);

        MenuItemDetailsViewModel? model = await _service.GetDishDetailsAsync(dish.Id);

        model.ShouldNotBeNull();
        model.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task GetDishDetailsAsync_ShouldReturnNull_WhenTheDishDoesNotExist()
    {
        _menu.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MenuItem?)null);

        MenuItemDetailsViewModel? model = await _service.GetDishDetailsAsync(Guid.NewGuid());

        model.ShouldBeNull();
    }

    private static MenuItem NewDish(Category category, string name, decimal price, bool isVegetarian, bool isAvailable) => new()
    {
        Id = Guid.NewGuid(),
        CategoryId = category.Id,
        Category = category,
        Name = name,
        Price = price,
        IsVegetarian = isVegetarian,
        IsAvailable = isAvailable
    };
}
