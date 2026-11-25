using System.Security.Claims;
using System.Text;
using Client.Web;
using Client.Web.Hubs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Contracts.Enums;
using Shouldly;

namespace FoodOrdering.UnitTests.Client;

public class ServiceExtensionsTests
{
    [Fact]
    public void AddClientSignalR_ShouldSendTheOrderStatusUpdatedArgumentsAsJsonWithTheStatusAsText()
    {
        using ServiceProvider services = new ServiceCollection().AddLogging().AddClientSignalR().BuildServiceProvider();
        IHubProtocol json = services.GetServices<IHubProtocol>().Single(p => p.Name == "json");
        Guid orderId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
        DateTime eta = new(2026, 11, 24, 13, 15, 0, DateTimeKind.Utc);

        string payload = Encoding.UTF8.GetString(json.GetMessageBytes(
            new InvocationMessage(OrderHub.OrderStatusUpdatedEvent, [orderId, OrderStatus.Accepted, eta])).Span);

        payload.ShouldContain("\"target\":\"OrderStatusUpdated\"");
        payload.ShouldContain("\"arguments\":[\"7c9e6679-7425-40de-944b-e07fc1f90ae7\",\"Accepted\",\"2026-11-24T13:15:00Z\"]");
    }

    [Fact]
    public void AddClientSignalR_ShouldRegisterTheHubNotifier()
    {
        using ServiceProvider services = new ServiceCollection().AddLogging().AddClientSignalR().BuildServiceProvider();

        services.GetRequiredService<IOrderNotifier>().ShouldBeOfType<OrderHubNotifier>();
    }

    [Fact]
    public void AddClientIdentity_ShouldUseTheNameIdentifierClaimThatSignalRUsesAsTheUserId()
    {
        using ServiceProvider services = new ServiceCollection().AddLogging().AddClientIdentity().AddClientSignalR().BuildServiceProvider();

        services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.UserIdClaimType.ShouldBe(ClaimTypes.NameIdentifier);
        services.GetRequiredService<IUserIdProvider>().ShouldBeOfType<DefaultUserIdProvider>();
    }
}
