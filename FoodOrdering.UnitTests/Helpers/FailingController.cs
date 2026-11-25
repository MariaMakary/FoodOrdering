using Microsoft.AspNetCore.Mvc;

namespace FoodOrdering.UnitTests.Helpers;

public class FailingController : Controller
{
    public const string SecretDetail = "Connection to db-secret-host failed";

    public IActionResult Throw() => throw new InvalidOperationException(SecretDetail);

    public IActionResult Bad() => BadRequest();
}
