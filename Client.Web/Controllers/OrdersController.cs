using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Client.Web.Controllers;

[Authorize(Roles = ClientRoles.Customer)]
public class OrdersController(IOrderService orders) : Controller
{
    public const string CancelledMessage = "Your order was cancelled.";

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        IReadOnlyList<OrderListItemViewModel> model = await orders.GetCustomerOrdersAsync(User.GetCustomerId(), ct);
        return View(model);
    }

    [HttpGet("Orders/Details/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        OrderDetailsViewModel? model = await orders.GetOrderDetailsAsync(id, User.GetCustomerId(), ct);
        if (model is null)
        {
            return NotFound();
        }

        return View(model);
    }

    [HttpPost("Orders/Cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        ServiceResult result = await orders.CancelOrderAsync(id, User.GetCustomerId(), ct);
        if (result.Error == OrderErrors.OrderNotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            TempData[TempDataKeys.Success] = CancelledMessage;
        }
        else
        {
            TempData[TempDataKeys.Error] = result.Error!.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
