using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Client.Web.Services;
using NSubstitute;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;
using Shouldly;

namespace FoodOrdering.UnitTests.Client.Services;

public class RatingServiceTests
{
    private readonly IRatingRepository _ratings = Substitute.For<IRatingRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly RatingService _service;
    private readonly ApplicationUser _customer;
    private readonly MenuItem _burger;
    private readonly MenuItem _lemonade;
    private readonly List<Rating> _addedRatings = [];
    private readonly List<DishRated> _messages = [];

    public RatingServiceTests()
    {
        _service = new RatingService(_ratings, _outbox);
        _customer = new ApplicationUser { Id = Guid.NewGuid(), FirstName = "Sara", LastName = "Haddad" };
        _burger = new MenuItem { Id = Guid.NewGuid(), Name = "Chicken Burger", PhotoUrl = "/images/burger.jpg" };
        _lemonade = new MenuItem { Id = Guid.NewGuid(), Name = "Lemonade" };

        _ratings.GetCustomerAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        _ratings.Add(Arg.Do<Rating>(_addedRatings.Add));
        _ratings.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(true);
        _outbox.Add(Arg.Do<MessageBase>(m => _messages.Add(m.ShouldBeOfType<DishRated>())));
    }

    [Fact]
    public async Task GetRateFormAsync_ShouldListEachUnratedDishOnce()
    {
        Order order = GivenOrder(OrderStatus.Delivered);
        order.Items.Add(NewItem(order, _burger));
        order.Ratings.Add(new Rating { Id = Guid.NewGuid(), OrderId = order.Id, MenuItemId = _lemonade.Id, Stars = 4 });

        ServiceResult<RateOrderViewModel> result = await _service.GetRateFormAsync(order.Id, _customer.Id);

        result.Succeeded.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.OrderId.ShouldBe(order.Id);
        result.Value.OrderNumber.ShouldBe("FO-1000");
        RateDishViewModel dish = result.Value.Dishes.ShouldHaveSingleItem();
        dish.MenuItemId.ShouldBe(_burger.Id);
        dish.Name.ShouldBe("Chicken Burger");
        dish.PhotoUrl.ShouldBe("/images/burger.jpg");
        dish.Stars.ShouldBeNull();
    }

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.OutForDelivery)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task GetRateFormAsync_ShouldReturnNotFound_WhenTheOrderIsNotDelivered(OrderStatus status)
    {
        Order order = GivenOrder(status);

        ServiceResult<RateOrderViewModel> result = await _service.GetRateFormAsync(order.Id, _customer.Id);

        result.Error.ShouldBe(RatingErrors.OrderNotFound);
    }

    [Fact]
    public async Task GetRateFormAsync_ShouldReturnNotFound_WhenTheOrderBelongsToAnotherCustomer()
    {
        Order order = GivenOrder(OrderStatus.Delivered, Guid.NewGuid());

        ServiceResult<RateOrderViewModel> result = await _service.GetRateFormAsync(order.Id, _customer.Id);

        result.Error.ShouldBe(RatingErrors.OrderNotFound);
    }

    [Fact]
    public async Task GetRateFormAsync_ShouldReturnNotFound_WhenTheOrderDoesNotExist()
    {
        ServiceResult<RateOrderViewModel> result = await _service.GetRateFormAsync(Guid.NewGuid(), _customer.Id);

        result.Error.ShouldBe(RatingErrors.OrderNotFound);
    }

    [Fact]
    public async Task GetRateFormAsync_ShouldRefuse_WhenEveryDishIsAlreadyRated()
    {
        Order order = GivenOrder(OrderStatus.Delivered);
        order.Ratings.Add(new Rating { Id = Guid.NewGuid(), OrderId = order.Id, MenuItemId = _burger.Id, Stars = 5 });
        order.Ratings.Add(new Rating { Id = Guid.NewGuid(), OrderId = order.Id, MenuItemId = _lemonade.Id, Stars = 4 });

        ServiceResult<RateOrderViewModel> result = await _service.GetRateFormAsync(order.Id, _customer.Id);

        result.Error.ShouldBe(RatingErrors.AlreadyRated);
    }

    [Fact]
    public async Task SubmitAsync_ShouldAddOneRatingAndOneDishRatedToTheOutboxPerDishThenSaveOnce()
    {
        Order order = GivenOrder(OrderStatus.Delivered);
        DateTime before = DateTime.UtcNow;

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order,
            (_burger.Id, 5, "  Best burger in town!  "),
            (_lemonade.Id, 3, "   ")));

        result.Succeeded.ShouldBeTrue();
        _outbox.Received(2).Add(Arg.Any<DishRated>());
        _addedRatings.Count.ShouldBe(2);
        _messages.Count.ShouldBe(2);

        Rating burger = _addedRatings.Single(r => r.MenuItemId == _burger.Id);
        burger.Id.ShouldNotBe(Guid.Empty);
        burger.OrderId.ShouldBe(order.Id);
        burger.CustomerId.ShouldBe(_customer.Id);
        burger.Stars.ShouldBe(5);
        burger.Comment.ShouldBe("Best burger in town!");
        _addedRatings.Single(r => r.MenuItemId == _lemonade.Id).Comment.ShouldBeNull();

        DishRated burgerMessage = _messages.Single(m => m.MenuItemId == _burger.Id);
        burgerMessage.RatingId.ShouldBe(burger.Id);
        burgerMessage.OrderId.ShouldBe(order.Id);
        burgerMessage.CorrelationId.ShouldBe(order.Id);
        burgerMessage.CustomerName.ShouldBe("Sara Haddad");
        burgerMessage.Stars.ShouldBe(5);
        burgerMessage.Comment.ShouldBe("Best burger in town!");
        burgerMessage.RatedAt.ShouldBeGreaterThanOrEqualTo(before);
        burgerMessage.CreatedAt.ShouldBe(burgerMessage.RatedAt);
        _messages.Select(m => m.RatedAt).Distinct().ShouldHaveSingleItem();
        _messages.Select(m => m.RatingId).ShouldBe(_addedRatings.Select(r => r.Id), ignoreOrder: true);

        await _ratings.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_ShouldSaveOnlyTheRatedDishes_WhenTheCustomerSkipsADish()
    {
        Order order = GivenOrder(OrderStatus.Delivered);

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order,
            (_burger.Id, 4, "Tasty"),
            (_lemonade.Id, null, "ignored")));

        result.Succeeded.ShouldBeTrue();
        _addedRatings.ShouldHaveSingleItem().MenuItemId.ShouldBe(_burger.Id);
        _messages.ShouldHaveSingleItem().MenuItemId.ShouldBe(_burger.Id);
        await _ratings.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_ShouldRefuse_WhenEveryDishIsSkipped()
    {
        Order order = GivenOrder(OrderStatus.Delivered);

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order,
            (_burger.Id, null, "Nice"),
            (_lemonade.Id, null, null)));

        result.Error.ShouldBe(RatingErrors.NothingRated);
        await AssertNothingWasSavedAsync();
    }

    [Fact]
    public async Task SubmitAsync_ShouldRateTheOtherDish_WhenTheSkippedDishIsAlreadyRated()
    {
        Order order = GivenOrder(OrderStatus.Delivered);
        order.Ratings.Add(new Rating { Id = Guid.NewGuid(), OrderId = order.Id, MenuItemId = _burger.Id, Stars = 5 });

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order,
            (_burger.Id, null, null),
            (_lemonade.Id, 3, null)));

        result.Succeeded.ShouldBeTrue();
        _addedRatings.ShouldHaveSingleItem().MenuItemId.ShouldBe(_lemonade.Id);
    }

    [Fact]
    public async Task SubmitAsync_ShouldRefuse_WhenTheUniqueIndexRejectsTheSave()
    {
        Order order = GivenOrder(OrderStatus.Delivered);
        _ratings.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(false);

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order, (_burger.Id, 4, null)));

        result.Error.ShouldBe(RatingErrors.AlreadyRated);
    }

    [Fact]
    public async Task SubmitAsync_ShouldRefuse_WhenADishIsAlreadyRated()
    {
        Order order = GivenOrder(OrderStatus.Delivered);
        order.Ratings.Add(new Rating { Id = Guid.NewGuid(), OrderId = order.Id, MenuItemId = _burger.Id, Stars = 5 });

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order,
            (_burger.Id, 2, null),
            (_lemonade.Id, 4, null)));

        result.Error.ShouldBe(RatingErrors.AlreadyRated);
        await AssertNothingWasSavedAsync();
    }

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task SubmitAsync_ShouldReturnNotFound_WhenTheOrderIsNotDelivered(OrderStatus status)
    {
        Order order = GivenOrder(status);

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order, (_burger.Id, 5, null)));

        result.Error.ShouldBe(RatingErrors.OrderNotFound);
        await AssertNothingWasSavedAsync();
    }

    [Fact]
    public async Task SubmitAsync_ShouldReturnNotFound_WhenTheOrderBelongsToAnotherCustomer()
    {
        Order order = GivenOrder(OrderStatus.Delivered, Guid.NewGuid());

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order, (_burger.Id, 5, null)));

        result.Error.ShouldBe(RatingErrors.OrderNotFound);
        await AssertNothingWasSavedAsync();
    }

    [Fact]
    public async Task SubmitAsync_ShouldRefuse_WhenADishIsNotPartOfTheOrder()
    {
        Order order = GivenOrder(OrderStatus.Delivered);

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order, (Guid.NewGuid(), 5, null)));

        result.Error.ShouldBe(RatingErrors.InvalidDishes);
        await AssertNothingWasSavedAsync();
    }

    [Fact]
    public async Task SubmitAsync_ShouldRefuse_WhenTheSameDishIsPostedTwice()
    {
        Order order = GivenOrder(OrderStatus.Delivered);

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order,
            (_burger.Id, 5, null),
            (_burger.Id, 1, null)));

        result.Error.ShouldBe(RatingErrors.InvalidDishes);
        await AssertNothingWasSavedAsync();
    }

    [Fact]
    public async Task SubmitAsync_ShouldRefuse_WhenNoDishIsPosted()
    {
        Order order = GivenOrder(OrderStatus.Delivered);

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order));

        result.Error.ShouldBe(RatingErrors.InvalidDishes);
        await AssertNothingWasSavedAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SubmitAsync_ShouldRefuse_WhenTheStarsAreOutOfRange(int stars)
    {
        Order order = GivenOrder(OrderStatus.Delivered);

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order, (_burger.Id, stars, null)));

        result.Error.ShouldBe(RatingErrors.InvalidStars);
        await AssertNothingWasSavedAsync();
    }

    [Fact]
    public async Task SubmitAsync_ShouldRefuse_WhenTheCustomerNoLongerExists()
    {
        Order order = GivenOrder(OrderStatus.Delivered);
        _ratings.GetCustomerAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);

        ServiceResult result = await _service.SubmitAsync(_customer.Id, NewForm(order, (_burger.Id, 5, null)));

        result.Error.ShouldBe(RatingErrors.CustomerNotFound);
        await AssertNothingWasSavedAsync();
    }

    private Order GivenOrder(OrderStatus status, Guid? customerId = null)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "FO-1000",
            CustomerId = customerId ?? _customer.Id,
            Status = status
        };
        order.Items.Add(NewItem(order, _burger));
        order.Items.Add(NewItem(order, _lemonade));
        _ratings.GetOrderForRatingAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private static OrderItem NewItem(Order order, MenuItem dish) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        MenuItemId = dish.Id,
        Name = dish.Name,
        Quantity = 1,
        MenuItem = dish
    };

    private static RateOrderViewModel NewForm(Order order, params (Guid MenuItemId, int? Stars, string? Comment)[] dishes) => new()
    {
        OrderId = order.Id,
        Dishes = dishes
            .Select(d => new RateDishViewModel { MenuItemId = d.MenuItemId, Stars = d.Stars, Comment = d.Comment })
            .ToList()
    };

    private async Task AssertNothingWasSavedAsync()
    {
        _ratings.DidNotReceive().Add(Arg.Any<Rating>());
        _outbox.DidNotReceive().Add(Arg.Any<MessageBase>());
        await _ratings.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
