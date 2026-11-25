using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Client.Web.Controllers;

[Authorize(Roles = ClientRoles.Customer)]
public class CartController(ICartService cart) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        CartViewModel model = await cart.GetCartAsync(User.GetCustomerId(), ct);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(AddToCartViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToCartWithError(FirstModelError());
        }

        ServiceResult result = await cart.AddItemAsync(User.GetCustomerId(), model, ct);
        if (!result.Succeeded)
        {
            return RedirectToCartWithError(result.Error!.Message);
        }

        TempData[TempDataKeys.Success] = "The dish was added to your cart.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(UpdateCartItemViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToCartWithError(FirstModelError());
        }

        ServiceResult result = await cart.UpdateQuantityAsync(User.GetCustomerId(), model, ct);
        if (!result.Succeeded)
        {
            return RedirectToCartWithError(result.Error!.Message);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Cart/Remove/{cartItemId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid cartItemId, CancellationToken ct)
    {
        ServiceResult result = await cart.RemoveItemAsync(User.GetCustomerId(), cartItemId, ct);
        if (!result.Succeeded)
        {
            return RedirectToCartWithError(result.Error!.Message);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await cart.ClearAsync(User.GetCustomerId(), ct);
        return RedirectToAction(nameof(Index));
    }

    private string FirstModelError() =>
        ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
        ?? "Your cart could not be updated. Please try again.";

    private RedirectToActionResult RedirectToCartWithError(string message)
    {
        TempData[TempDataKeys.Error] = message;
        return RedirectToAction(nameof(Index));
    }
}
