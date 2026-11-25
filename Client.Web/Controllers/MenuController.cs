using Client.Web.Models.ViewModels;
using Client.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Client.Web.Controllers;

[AllowAnonymous]
public class MenuController(IMenuService menu) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        Guid? category,
        string? search,
        bool vegetarian,
        decimal? maxPrice,
        CancellationToken ct)
    {
        MenuIndexViewModel model = await menu.GetMenuAsync(category, search, vegetarian, maxPrice, ct);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        MenuItemDetailsViewModel? model = await menu.GetDishDetailsAsync(id, ct);
        if (model is null)
        {
            return NotFound();
        }

        return View(model);
    }
}
