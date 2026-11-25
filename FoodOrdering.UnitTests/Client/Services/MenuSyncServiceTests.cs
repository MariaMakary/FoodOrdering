using Client.Web.Models.Entities;
using Client.Web.Repositories;
using Client.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shouldly;

namespace FoodOrdering.UnitTests.Client.Services;

public class MenuSyncServiceTests
{
    private static readonly DateTime StoredAt = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly CategoryDto Burgers = new() { Id = Guid.NewGuid(), Name = "Burgers", SortOrder = 1 };

    private readonly IMenuSyncRepository _menu = Substitute.For<IMenuSyncRepository>();
    private readonly MenuSyncService _service;

    public MenuSyncServiceTests()
    {
        _service = new MenuSyncService(_menu, NullLogger<MenuSyncService>.Instance);
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldAddTheDishCategoryAndExtrasWithTheAdminIds_WhenTheDishIsNew()
    {
        MenuItemExtraDto cheese = Extra("Extra cheese", 1.00m);
        MenuItemUpdated message = Snapshot(MenuItemAction.Created, Guid.NewGuid(), StoredAt) with { Extras = [cheese] };

        await _service.ApplyMenuItemUpdatedAsync(message);

        MenuItem dish = Added<MenuItem>(nameof(IMenuSyncRepository.AddMenuItem)).ShouldHaveSingleItem();
        dish.Id.ShouldBe(message.MenuItemId);
        dish.CategoryId.ShouldBe(Burgers.Id);
        dish.Name.ShouldBe("Chicken Burger");
        dish.Description.ShouldBe("Grilled chicken breast");
        dish.Price.ShouldBe(9.50m);
        dish.PhotoUrl.ShouldBe("https://localhost:7101/images/menu/chicken-burger.jpg");
        dish.IsVegetarian.ShouldBeTrue();
        dish.IsAvailable.ShouldBeTrue();
        dish.IsDeleted.ShouldBeFalse();
        dish.SourceUpdatedAt.ShouldBe(StoredAt);
        Category category = Added<Category>(nameof(IMenuSyncRepository.AddCategory)).ShouldHaveSingleItem();
        category.Id.ShouldBe(Burgers.Id);
        category.Name.ShouldBe("Burgers");
        category.SortOrder.ShouldBe(1);
        MenuItemExtra extra = Added<MenuItemExtra>(nameof(IMenuSyncRepository.AddExtra)).ShouldHaveSingleItem();
        extra.Id.ShouldBe(cheese.Id);
        extra.MenuItemId.ShouldBe(message.MenuItemId);
        extra.Name.ShouldBe("Extra cheese");
        extra.Price.ShouldBe(1.00m);
        await _menu.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldUpdateTheExistingCategory_WhenItsNameOrSortOrderChanged()
    {
        var category = new Category { Id = Burgers.Id, Name = "Burgers", SortOrder = 1 };
        _menu.GetCategoryAsync(Burgers.Id, Arg.Any<CancellationToken>()).Returns(category);

        await _service.ApplyMenuItemUpdatedAsync(Snapshot(MenuItemAction.Created, Guid.NewGuid(), StoredAt) with
        {
            Category = Burgers with { Name = "Grill", SortOrder = 3 }
        });

        category.Name.ShouldBe("Grill");
        category.SortOrder.ShouldBe(3);
        _menu.DidNotReceive().AddCategory(Arg.Any<Category>());
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldUpdateKeptExtrasAddNewOnesAndRemoveMissingOnes()
    {
        MenuItem dish = GivenDish(StoredAt);
        MenuItemExtra cheese = GivenExtra(dish, "Extra cheese", 1.00m);
        MenuItemExtra bacon = GivenExtra(dish, "Bacon", 1.50m);
        MenuItemExtraDto pickles = Extra("Pickles", 0.50m);

        await _service.ApplyMenuItemUpdatedAsync(Snapshot(MenuItemAction.Updated, dish.Id, StoredAt.AddMinutes(1)) with
        {
            Extras = [new MenuItemExtraDto { Id = cheese.Id, Name = "Cheddar", Price = 1.25m }, pickles]
        });

        cheese.Name.ShouldBe("Cheddar");
        cheese.Price.ShouldBe(1.25m);
        _menu.Received(1).RemoveExtra(bacon);
        _menu.DidNotReceive().RemoveExtra(cheese);
        Added<MenuItemExtra>(nameof(IMenuSyncRepository.AddExtra)).ShouldHaveSingleItem().Id.ShouldBe(pickles.Id);
        dish.Extras.Select(e => e.Id).ShouldBe([cheese.Id, pickles.Id], ignoreOrder: true);
        _menu.DidNotReceive().AddMenuItem(Arg.Any<MenuItem>());
        await _menu.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldRemoveEveryExtra_WhenTheMessageHasNone()
    {
        MenuItem dish = GivenDish(StoredAt);
        MenuItemExtra cheese = GivenExtra(dish, "Extra cheese", 1.00m);

        await _service.ApplyMenuItemUpdatedAsync(Snapshot(MenuItemAction.Updated, dish.Id, StoredAt.AddMinutes(1)) with { Extras = [] });

        _menu.Received(1).RemoveExtra(cheese);
        dish.Extras.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldUpdateTheDish_WhenACreatedArrivesForAnExistingDish()
    {
        MenuItem dish = GivenDish(StoredAt);

        await _service.ApplyMenuItemUpdatedAsync(Snapshot(MenuItemAction.Created, dish.Id, StoredAt.AddMinutes(1)) with { Price = 11.00m });

        dish.Price.ShouldBe(11.00m);
        dish.SourceUpdatedAt.ShouldBe(StoredAt.AddMinutes(1));
        _menu.DidNotReceive().AddMenuItem(Arg.Any<MenuItem>());
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldBringTheDishBack_WhenANewerUpdateFollowsADelete()
    {
        MenuItem dish = GivenDish(StoredAt);
        dish.IsDeleted = true;
        dish.IsAvailable = false;

        await _service.ApplyMenuItemUpdatedAsync(Snapshot(MenuItemAction.Updated, dish.Id, StoredAt.AddMinutes(1)));

        dish.IsDeleted.ShouldBeFalse();
        dish.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldMarkTheDishUnavailableAndDeletedAndKeepItsData_WhenItIsDeleted()
    {
        MenuItem dish = GivenDish(StoredAt);
        MenuItemExtra cheese = GivenExtra(dish, "Extra cheese", 1.00m);

        await _service.ApplyMenuItemUpdatedAsync(Deleted(dish.Id, StoredAt.AddMinutes(1)));

        dish.IsAvailable.ShouldBeFalse();
        dish.IsDeleted.ShouldBeTrue();
        dish.SourceUpdatedAt.ShouldBe(StoredAt.AddMinutes(1));
        dish.Name.ShouldBe("Chicken Burger");
        dish.Extras.ShouldHaveSingleItem().ShouldBeSameAs(cheese);
        _menu.DidNotReceive().RemoveExtra(Arg.Any<MenuItemExtra>());
        _menu.DidNotReceive().AddCategory(Arg.Any<Category>());
        await _menu.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldDoNothing_WhenADeletedArrivesForAnUnknownDish()
    {
        await _service.ApplyMenuItemUpdatedAsync(Deleted(Guid.NewGuid(), StoredAt));

        _menu.DidNotReceive().AddMenuItem(Arg.Any<MenuItem>());
        await _menu.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(MenuItemAction.Created, 0)]
    [InlineData(MenuItemAction.Updated, 0)]
    [InlineData(MenuItemAction.Updated, -1)]
    [InlineData(MenuItemAction.Deleted, 0)]
    [InlineData(MenuItemAction.Deleted, -1)]
    public async Task ApplyMenuItemUpdatedAsync_ShouldDoNothing_WhenTheMessageIsNotNewerThanTheStoredSourceUpdatedAt(
        MenuItemAction action,
        int minutesAfterStored)
    {
        MenuItem dish = GivenDish(StoredAt);
        MenuItemExtra cheese = GivenExtra(dish, "Extra cheese", 1.00m);
        DateTime updatedAt = StoredAt.AddMinutes(minutesAfterStored);
        MenuItemUpdated message = action == MenuItemAction.Deleted
            ? Deleted(dish.Id, updatedAt)
            : Snapshot(action, dish.Id, updatedAt) with { Price = 1.00m, Extras = [] };

        await _service.ApplyMenuItemUpdatedAsync(message);

        dish.Price.ShouldBe(9.50m);
        dish.IsDeleted.ShouldBeFalse();
        dish.SourceUpdatedAt.ShouldBe(StoredAt);
        dish.Extras.ShouldHaveSingleItem().ShouldBeSameAs(cheese);
        await _menu.DidNotReceive().GetCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _menu.DidNotReceive().RemoveExtra(Arg.Any<MenuItemExtra>());
        await _menu.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldDoNothing_WhenTheMessageDiffersFromTheStoredTimeOnlyBelowAMicrosecond()
    {
        MenuItem dish = GivenDish(StoredAt.AddTicks(1230));

        await _service.ApplyMenuItemUpdatedAsync(Snapshot(MenuItemAction.Updated, dish.Id, StoredAt.AddTicks(1234)) with { Price = 1.00m });

        dish.Price.ShouldBe(9.50m);
        await _menu.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldStoreTheUpdatedAtRoundedDownToAMicrosecond()
    {
        await _service.ApplyMenuItemUpdatedAsync(Snapshot(MenuItemAction.Created, Guid.NewGuid(), StoredAt.AddTicks(1234)));

        Added<MenuItem>(nameof(IMenuSyncRepository.AddMenuItem)).ShouldHaveSingleItem().SourceUpdatedAt.ShouldBe(StoredAt.AddTicks(1230));
    }

    [Fact]
    public async Task ApplyMenuItemUpdatedAsync_ShouldThrow_WhenACreatedHasNoCategory()
    {
        MenuItemUpdated message = Snapshot(MenuItemAction.Created, Guid.NewGuid(), StoredAt) with { Category = null };

        await Should.ThrowAsync<InvalidOperationException>(() => _service.ApplyMenuItemUpdatedAsync(message));

        await _menu.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private MenuItem GivenDish(DateTime sourceUpdatedAt)
    {
        var dish = new MenuItem
        {
            Id = Guid.NewGuid(),
            CategoryId = Burgers.Id,
            Name = "Chicken Burger",
            Price = 9.50m,
            IsAvailable = true,
            SourceUpdatedAt = sourceUpdatedAt
        };
        _menu.GetMenuItemWithExtrasAsync(dish.Id, Arg.Any<CancellationToken>()).Returns(dish);
        return dish;
    }

    private static MenuItemExtra GivenExtra(MenuItem dish, string name, decimal price)
    {
        var extra = new MenuItemExtra { Id = Guid.NewGuid(), MenuItemId = dish.Id, Name = name, Price = price };
        dish.Extras.Add(extra);
        return extra;
    }

    private List<T> Added<T>(string methodName) =>
        _menu.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == methodName)
            .Select(call => call.GetArguments()[0].ShouldBeOfType<T>())
            .ToList();

    private static MenuItemExtraDto Extra(string name, decimal price) => new() { Id = Guid.NewGuid(), Name = name, Price = price };

    private static MenuItemUpdated Snapshot(MenuItemAction action, Guid menuItemId, DateTime updatedAt) => new()
    {
        Action = action,
        MenuItemId = menuItemId,
        Name = "Chicken Burger",
        Description = "Grilled chicken breast",
        Price = 9.50m,
        PhotoUrl = "https://localhost:7101/images/menu/chicken-burger.jpg",
        IsVegetarian = true,
        IsAvailable = true,
        Category = Burgers,
        Extras = [],
        UpdatedAt = updatedAt
    };

    private static MenuItemUpdated Deleted(Guid menuItemId, DateTime updatedAt) => new()
    {
        Action = MenuItemAction.Deleted,
        MenuItemId = menuItemId,
        IsAvailable = false,
        UpdatedAt = updatedAt
    };
}
