using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Client.Web.Controllers;

[Authorize(Roles = ClientRoles.Customer)]
public class RatingsController(IRatingService ratings, IAccountService accounts) : Controller
{
    public const string ThanksMessage = "Thanks for rating your order!";

    [HttpGet("Ratings/Create/{orderId:guid}")]
    public async Task<IActionResult> Create(Guid orderId, CancellationToken ct)
    {
        ServiceResult<RateOrderViewModel> result = await ratings.GetRateFormAsync(orderId, User.GetCustomerId(), ct);
        if (!result.Succeeded)
        {
            return await HandleFailureAsync(orderId, result.Error!);
        }

        return View(result.Value);
    }

    [HttpPost("Ratings/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RateOrderViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        ServiceResult result = await ratings.SubmitAsync(User.GetCustomerId(), model, ct);
        if (!result.Succeeded)
        {
            if (IsFormError(result.Error!))
            {
                ModelState.AddModelError(string.Empty, result.Error!.Message);
                return View(model);
            }

            return await HandleFailureAsync(model.OrderId, result.Error!);
        }

        TempData[TempDataKeys.Success] = ThanksMessage;
        return RedirectToOrder(model.OrderId);
    }

    private async Task<IActionResult> HandleFailureAsync(Guid orderId, ServiceError error)
    {
        if (error == RatingErrors.OrderNotFound)
        {
            return NotFound();
        }

        if (error == RatingErrors.CustomerNotFound)
        {
            await accounts.LogoutAsync();
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        TempData[TempDataKeys.Error] = error.Message;
        return RedirectToOrder(orderId);
    }

    private static bool IsFormError(ServiceError error) =>
        error == RatingErrors.NothingRated || error == RatingErrors.InvalidStars || error == RatingErrors.InvalidDishes;

    private RedirectToActionResult RedirectToOrder(Guid orderId) =>
        RedirectToAction(nameof(OrdersController.Details), "Orders", new { id = orderId });
}
