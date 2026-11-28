using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Enums;

namespace Client.Web.Controllers;

[Authorize(Roles = ClientRoles.Customer)]
public class CheckoutController(IOrderService orders, ICartService cart, IAccountService accounts) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ServiceResult<CheckoutViewModel> result = await orders.GetCheckoutAsync(User.GetCustomerId(), ct);
        if (!result.Succeeded)
        {
            return await HandleFailureAsync(result.Error!);
        }

        return View(result.Value);
    }

    [HttpPost]
    [ActionName(nameof(Index))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(CheckoutViewModel model, CancellationToken ct)
    {
        Guid customerId = User.GetCustomerId();

        if (!ModelState.IsValid)
        {
            return await RedisplayAsync(customerId, model, ct);
        }

        ServiceResult<Guid> result = await orders.PlaceOrderAsync(customerId, model, ct);
        if (result.Succeeded)
        {
            return model.PaymentMethod == PaymentMethod.Online
                ? LocalRedirect($"/Payment/Checkout/{result.Value}")
                : RedirectToAction(nameof(Confirmation), new { orderId = result.Value });
        }

        if (result.Error == OrderErrors.AddressRequired)
        {
            ModelState.AddModelError(nameof(CheckoutViewModel.Address), result.Error.Message);
            return await RedisplayAsync(customerId, model, ct);
        }

        return await HandleFailureAsync(result.Error!);
    }

    [HttpGet("Checkout/Confirmation/{orderId:guid}")]
    public async Task<IActionResult> Confirmation(Guid orderId, CancellationToken ct)
    {
        OrderConfirmationViewModel? model = await orders.GetConfirmationAsync(orderId, User.GetCustomerId(), ct);
        if (model is null)
        {
            return NotFound();
        }

        return View(model);
    }

    private async Task<IActionResult> RedisplayAsync(Guid customerId, CheckoutViewModel model, CancellationToken ct)
    {
        model.Cart = await cart.GetCartAsync(customerId, ct);
        if (model.Cart.IsEmpty)
        {
            return await HandleFailureAsync(OrderErrors.EmptyCart);
        }

        return View(nameof(Index), model);
    }

    private async Task<IActionResult> HandleFailureAsync(ServiceError error)
    {
        if (error == OrderErrors.CustomerNotFound)
        {
            await accounts.LogoutAsync();
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        TempData[TempDataKeys.Error] = error.Message;
        return RedirectToAction(nameof(CartController.Index), "Cart");
    }
}
