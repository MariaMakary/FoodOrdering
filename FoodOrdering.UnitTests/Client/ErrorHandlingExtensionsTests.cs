using Client.Web;
using Client.Web.Controllers;
using Client.Web.Services;
using FoodOrdering.UnitTests.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace FoodOrdering.UnitTests.Client;

public class ErrorHandlingExtensionsTests : ErrorPagesTestsBase
{
    protected override string ApplicationName => typeof(HomeController).Assembly.GetName().Name!;

    protected override string HomeControllerCategory => typeof(HomeController).FullName!;

    protected override void UseErrorPages(WebApplication app) => app.UseErrorPages();

    protected override void AddHomeControllerServices(IServiceCollection services) =>
        services.AddSingleton(Substitute.For<IMenuService>());
}
