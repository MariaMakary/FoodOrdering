using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace FoodOrdering.UnitTests.Helpers;

public abstract partial class ErrorPagesTestsBase
{
    private const string ThrowPath = "/Failing/Throw";

    protected abstract string ApplicationName { get; }

    protected abstract string HomeControllerCategory { get; }

    protected abstract void UseErrorPages(WebApplication app);

    protected virtual void AddHomeControllerServices(IServiceCollection services)
    {
    }

    [Fact]
    public async Task UseErrorPages_ShouldShowTheFriendlyErrorPage_WhenAnActionThrowsOutsideDevelopment()
    {
        await using WebApplication app = await StartAsync(Environments.Production, new ListLoggerProvider());

        HttpResponseMessage response = await app.GetTestClient().GetAsync(ThrowPath);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain("Something went wrong");
        body.ShouldContain("Back to home");
        body.ShouldNotContain(FailingController.SecretDetail);
        body.ShouldNotContain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task UseErrorPages_ShouldLogTheExceptionOnceWithThePathAndTheShownRequestId_WhenAnActionThrows()
    {
        var logs = new ListLoggerProvider();
        await using WebApplication app = await StartAsync(Environments.Production, logs);

        HttpResponseMessage response = await app.GetTestClient().GetAsync(ThrowPath);
        string body = await response.Content.ReadAsStringAsync();

        LogEntry entry = logs.Entries.Where(e => e.Level >= LogLevel.Error).ShouldHaveSingleItem();
        entry.Category.ShouldBe(HomeControllerCategory);
        entry.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe(FailingController.SecretDetail);
        entry.Properties["Path"].ShouldBe(ThrowPath);
        entry.Properties["Method"].ShouldBe("GET");
        string shownRequestId = RequestIdOnPage().Match(body).Groups["id"].Value;
        shownRequestId.ShouldNotBeNullOrEmpty();
        entry.Properties["RequestId"].ShouldBe(shownRequestId);
    }

    [Fact]
    public async Task UseErrorPages_ShouldShowTheFriendlyErrorPage_WhenAPostThrows()
    {
        await using WebApplication app = await StartAsync(Environments.Production, new ListLoggerProvider());

        HttpResponseMessage response = await app.GetTestClient().PostAsync(ThrowPath, new FormUrlEncodedContent([]));
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain("Something went wrong");
    }

    [Fact]
    public async Task UseErrorPages_ShouldShowTheNotFoundPage_WhenTheUrlDoesNotExist()
    {
        await using WebApplication app = await StartAsync(Environments.Production, new ListLoggerProvider());

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/no/such/page");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain("Page not found");
        body.ShouldContain("Back to home");
    }

    [Fact]
    public async Task UseErrorPages_ShouldShowTheErrorPageAndKeepTheStatus_WhenAnActionReturnsAnEmptyErrorStatus()
    {
        await using WebApplication app = await StartAsync(Environments.Production, new ListLoggerProvider());

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/Failing/Bad");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Something went wrong");
    }

    [Fact]
    public async Task UseErrorPages_ShouldShowTheErrorPageWithoutLogging_WhenErrorIsOpenedDirectly()
    {
        var logs = new ListLoggerProvider();
        await using WebApplication app = await StartAsync(Environments.Production, logs);

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/Home/Error");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Something went wrong");
        logs.Entries.ShouldNotContain(e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task UseErrorPages_ShouldLeaveTheDeveloperExceptionPage_WhenInDevelopment()
    {
        await using WebApplication app = await StartAsync(Environments.Development, new ListLoggerProvider());

        HttpResponseMessage response = await app.GetTestClient().GetAsync(ThrowPath);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain(FailingController.SecretDetail);
        body.ShouldNotContain("Something went wrong");
    }

    private async Task<WebApplication> StartAsync(string environment, ListLoggerProvider logs)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = ApplicationName,
            EnvironmentName = environment
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(FailingController).Assembly);
        AddHomeControllerServices(builder.Services);

        WebApplication app = builder.Build();
        UseErrorPages(app);
        app.UseRouting();
        app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

        await app.StartAsync();
        return app;
    }

    [GeneratedRegex("<code>(?<id>[^<]+)</code>")]
    private static partial Regex RequestIdOnPage();
}
