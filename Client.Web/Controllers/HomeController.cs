using System.Diagnostics;
using Client.Web.Models;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Client.Web.Controllers;

public class HomeController(IMenuService menu, ILogger<HomeController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        HomeViewModel model = await menu.GetHomeAsync(ct);
        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        string requestId = CurrentRequestId();
        IExceptionHandlerPathFeature? failure = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (failure is not null)
        {
            logger.LogError(failure.Error, "Unhandled exception on {Method} {Path}, request id {RequestId}",
                Request.Method, failure.Path, requestId);
        }

        return View(new ErrorViewModel { RequestId = requestId });
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Route("Home/StatusCode/{code:int}")]
    public IActionResult StatusCodePage(int code)
    {
        if (code == StatusCodes.Status404NotFound)
        {
            return View("NotFound");
        }

        return View("Error", new ErrorViewModel { RequestId = CurrentRequestId() });
    }

    private string CurrentRequestId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
