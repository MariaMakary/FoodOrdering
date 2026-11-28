using System.Security.Claims;
using Client.Web.Controllers;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shared.Contracts.Enums;
using Shouldly;

namespace FoodOrdering.UnitTests.Client.Controllers;

public class CheckoutControllerTests
{
    private readonly IOrderService _orders = Substitute.For<IOrderService>();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly CheckoutController _controller;

    public CheckoutControllerTests()
    {
        _controller = new CheckoutController(_orders, Substitute.For<ICartService>(), Substitute.For<IAccountService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, _customerId.ToString())],
                        "Test"))
                }
            }
        };
    }

    [Fact]
    public async Task Confirmation_ShouldShowTheOrder_WhenItIsTheCustomersOwn()
    {
        Guid orderId = Guid.NewGuid();
        var model = new OrderConfirmationViewModel { OrderId = orderId, OrderNumber = "FO-1042" };
        _orders.GetConfirmationAsync(orderId, _customerId, Arg.Any<CancellationToken>()).Returns(model);

        IActionResult result = await _controller.Confirmation(orderId, CancellationToken.None);

        ViewResult view = result.ShouldBeOfType<ViewResult>();
        view.ViewName.ShouldBeNull();
        view.Model.ShouldBeSameAs(model);
    }

    [Fact]
    public async Task Confirmation_ShouldReturnNotFound_WhenTheServiceFindsNoOrderForTheCustomer()
    {
        Guid orderId = Guid.NewGuid();
        _orders.GetConfirmationAsync(orderId, _customerId, Arg.Any<CancellationToken>())
            .Returns((OrderConfirmationViewModel?)null);

        IActionResult result = await _controller.Confirmation(orderId, CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task PlaceOrder_ShouldRedirectToTheConfirmation_WhenTheOrderIsPaidInCash()
    {
        Guid orderId = Guid.NewGuid();
        CheckoutViewModel checkout = NewCheckout(PaymentMethod.Cash);
        _orders.PlaceOrderAsync(_customerId, checkout, Arg.Any<CancellationToken>())
            .Returns(ServiceResult<Guid>.Success(orderId));

        IActionResult result = await _controller.PlaceOrder(checkout, CancellationToken.None);

        RedirectToActionResult redirect = result.ShouldBeOfType<RedirectToActionResult>();
        redirect.ActionName.ShouldBe(nameof(CheckoutController.Confirmation));
        redirect.ControllerName.ShouldBeNull();
        redirect.RouteValues.ShouldNotBeNull();
        redirect.RouteValues["orderId"].ShouldBe(orderId);
    }

    [Fact]
    public async Task PlaceOrder_ShouldRedirectToThePayment_WhenTheOrderIsPaidOnline()
    {
        Guid orderId = Guid.NewGuid();
        CheckoutViewModel checkout = NewCheckout(PaymentMethod.Online);
        _orders.PlaceOrderAsync(_customerId, checkout, Arg.Any<CancellationToken>())
            .Returns(ServiceResult<Guid>.Success(orderId));

        IActionResult result = await _controller.PlaceOrder(checkout, CancellationToken.None);

        result.ShouldBeOfType<LocalRedirectResult>().Url.ShouldBe($"/Payment/Checkout/{orderId}");
    }

    private static CheckoutViewModel NewCheckout(PaymentMethod paymentMethod) => new()
    {
        DeliveryType = DeliveryType.Delivery,
        Address = "Hamra Street 12, Beirut",
        PaymentMethod = paymentMethod
    };
}
