using System.Security.Claims;
using Client.Web.Controllers;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using Shouldly;

namespace FoodOrdering.UnitTests.Client.Controllers;

public class RatingsControllerTests
{
    private readonly IRatingService _ratings = Substitute.For<IRatingService>();
    private readonly IAccountService _accounts = Substitute.For<IAccountService>();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly RatingsController _controller;

    public RatingsControllerTests()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, _customerId.ToString())],
                "Test"))
        };

        _controller = new RatingsController(_ratings, _accounts)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task CreateGet_ShouldShowTheForm_WhenTheOrderCanBeRated()
    {
        var form = new RateOrderViewModel { OrderId = _orderId, OrderNumber = "FO-1000" };
        _ratings.GetRateFormAsync(_orderId, _customerId, Arg.Any<CancellationToken>())
            .Returns(ServiceResult<RateOrderViewModel>.Success(form));

        IActionResult result = await _controller.Create(_orderId, CancellationToken.None);

        result.ShouldBeOfType<ViewResult>().Model.ShouldBeSameAs(form);
    }

    [Fact]
    public async Task CreateGet_ShouldReturnNotFound_WhenTheOrderCannotBeRated()
    {
        _ratings.GetRateFormAsync(_orderId, _customerId, Arg.Any<CancellationToken>())
            .Returns(ServiceResult<RateOrderViewModel>.Failure(RatingErrors.OrderNotFound));

        IActionResult result = await _controller.Create(_orderId, CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CreateGet_ShouldRedirectToTheOrderWithTheError_WhenEveryDishIsAlreadyRated()
    {
        _ratings.GetRateFormAsync(_orderId, _customerId, Arg.Any<CancellationToken>())
            .Returns(ServiceResult<RateOrderViewModel>.Failure(RatingErrors.AlreadyRated));

        IActionResult result = await _controller.Create(_orderId, CancellationToken.None);

        AssertRedirectToOrder(result);
        _controller.TempData[TempDataKeys.Error].ShouldBe(RatingErrors.AlreadyRated.Message);
    }

    [Fact]
    public async Task CreatePost_ShouldRedirectToTheOrderWithThanks_WhenTheRatingsAreSaved()
    {
        RateOrderViewModel form = NewForm();
        _ratings.SubmitAsync(_customerId, form, Arg.Any<CancellationToken>()).Returns(ServiceResult.Success());

        IActionResult result = await _controller.Create(form, CancellationToken.None);

        AssertRedirectToOrder(result);
        _controller.TempData[TempDataKeys.Success].ShouldBe(RatingsController.ThanksMessage);
    }

    [Fact]
    public async Task CreatePost_ShouldShowTheFormAgainWithoutSaving_WhenTheFormIsInvalid()
    {
        RateOrderViewModel form = NewForm();
        _controller.ModelState.AddModelError("Dishes[0].Stars", "Choose between 1 and 5 stars.");

        IActionResult result = await _controller.Create(form, CancellationToken.None);

        result.ShouldBeOfType<ViewResult>().Model.ShouldBeSameAs(form);
        await _ratings.DidNotReceive().SubmitAsync(Arg.Any<Guid>(), Arg.Any<RateOrderViewModel>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(nameof(RatingErrors.NothingRated))]
    [InlineData(nameof(RatingErrors.InvalidStars))]
    [InlineData(nameof(RatingErrors.InvalidDishes))]
    public async Task CreatePost_ShouldShowTheFormAgainWithTheError_WhenTheServiceRefusesTheForm(string errorName)
    {
        ServiceError error = errorName switch
        {
            nameof(RatingErrors.NothingRated) => RatingErrors.NothingRated,
            nameof(RatingErrors.InvalidStars) => RatingErrors.InvalidStars,
            _ => RatingErrors.InvalidDishes
        };
        RateOrderViewModel form = NewForm();
        _ratings.SubmitAsync(_customerId, form, Arg.Any<CancellationToken>()).Returns(ServiceResult.Failure(error));

        IActionResult result = await _controller.Create(form, CancellationToken.None);

        result.ShouldBeOfType<ViewResult>().Model.ShouldBeSameAs(form);
        _controller.ModelState[string.Empty].ShouldNotBeNull().Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(error.Message);
    }

    [Fact]
    public async Task CreatePost_ShouldRedirectToTheOrderWithTheError_WhenTheDishesAreAlreadyRated()
    {
        RateOrderViewModel form = NewForm();
        _ratings.SubmitAsync(_customerId, form, Arg.Any<CancellationToken>())
            .Returns(ServiceResult.Failure(RatingErrors.AlreadyRated));

        IActionResult result = await _controller.Create(form, CancellationToken.None);

        AssertRedirectToOrder(result);
        _controller.TempData[TempDataKeys.Error].ShouldBe(RatingErrors.AlreadyRated.Message);
    }

    [Fact]
    public async Task CreatePost_ShouldReturnNotFound_WhenTheOrderCannotBeRated()
    {
        RateOrderViewModel form = NewForm();
        _ratings.SubmitAsync(_customerId, form, Arg.Any<CancellationToken>())
            .Returns(ServiceResult.Failure(RatingErrors.OrderNotFound));

        IActionResult result = await _controller.Create(form, CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CreatePost_ShouldLogOutAndGoToLogin_WhenTheCustomerNoLongerExists()
    {
        RateOrderViewModel form = NewForm();
        _ratings.SubmitAsync(_customerId, form, Arg.Any<CancellationToken>())
            .Returns(ServiceResult.Failure(RatingErrors.CustomerNotFound));

        IActionResult result = await _controller.Create(form, CancellationToken.None);

        RedirectToActionResult redirect = result.ShouldBeOfType<RedirectToActionResult>();
        redirect.ActionName.ShouldBe(nameof(AccountController.Login));
        redirect.ControllerName.ShouldBe("Account");
        await _accounts.Received(1).LogoutAsync();
    }

    private RateOrderViewModel NewForm() => new()
    {
        OrderId = _orderId,
        Dishes = [new RateDishViewModel { MenuItemId = Guid.NewGuid(), Stars = 5 }]
    };

    private void AssertRedirectToOrder(IActionResult result)
    {
        RedirectToActionResult redirect = result.ShouldBeOfType<RedirectToActionResult>();
        redirect.ActionName.ShouldBe(nameof(OrdersController.Details));
        redirect.ControllerName.ShouldBe("Orders");
        redirect.RouteValues.ShouldNotBeNull();
        redirect.RouteValues["id"].ShouldBe(_orderId);
    }
}
