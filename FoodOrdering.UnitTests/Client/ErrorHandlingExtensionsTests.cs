using Client.Web;
using Client.Web.Controllers;
using FoodOrdering.UnitTests.Helpers;
using Microsoft.AspNetCore.Builder;

namespace FoodOrdering.UnitTests.Client;

public class ErrorHandlingExtensionsTests : ErrorPagesTestsBase
{
    protected override string ApplicationName => typeof(HomeController).Assembly.GetName().Name!;

    protected override string HomeControllerCategory => typeof(HomeController).FullName!;

    protected override void UseErrorPages(WebApplication app) => app.UseErrorPages();
}
