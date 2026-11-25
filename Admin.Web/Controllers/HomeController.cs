using System.Diagnostics;
using Admin.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Admin.Web.Controllers;

public class HomeController(ILogger<HomeController> logger) : Controller
{
    public IActionResult Index()
    {
        return View();
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
