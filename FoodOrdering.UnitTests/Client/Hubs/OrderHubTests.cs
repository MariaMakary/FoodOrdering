using System.Net;
using System.Security.Claims;
using Client.Web;
using Client.Web.Hubs;
using Client.Web.Models.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace FoodOrdering.UnitTests.Client.Hubs;

public class OrderHubTests
{
    private const string NegotiatePath = OrderHub.Path + "/negotiate?negotiateVersion=1";
    private const string TestRoleHeader = "X-Test-Role";

    [Fact]
    public async Task Negotiate_ShouldReturnUnauthorized_WhenNotLoggedIn()
    {
        await using WebApplication app = await StartAsync();

        HttpResponseMessage response = await app.GetTestClient().PostAsync(NegotiatePath, null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Negotiate_ShouldBeForbidden_WhenTheUserIsNotACustomer()
    {
        await using WebApplication app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, NegotiatePath);
        request.Headers.Add(TestRoleHeader, "Staff");

        HttpResponseMessage response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Negotiate_ShouldSucceed_WhenTheUserIsACustomer()
    {
        await using WebApplication app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, NegotiatePath);
        request.Headers.Add(TestRoleHeader, ClientRoles.Customer);

        HttpResponseMessage response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("connectionToken");
    }

    private static async Task<WebApplication> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddClientIdentity();
        builder.Services.AddClientSignalR();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue(TestRoleHeader, out var role))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.Role, role.ToString())
                    ],
                    "Test"));
            }

            return next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<OrderHub>(OrderHub.Path);

        await app.StartAsync();
        return app;
    }
}
