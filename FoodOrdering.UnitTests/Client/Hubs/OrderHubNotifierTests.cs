using Client.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Shared.Contracts.Enums;

namespace FoodOrdering.UnitTests.Client.Hubs;

public class OrderHubNotifierTests
{
    [Fact]
    public async Task OrderStatusUpdatedAsync_ShouldSendTheEventToTheCustomerOnly()
    {
        var hub = Substitute.For<IHubContext<OrderHub>>();
        var customerProxy = Substitute.For<IClientProxy>();
        Guid customerId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTime eta = new(2026, 11, 24, 13, 15, 0, DateTimeKind.Utc);
        hub.Clients.User(customerId.ToString()).Returns(customerProxy);

        await new OrderHubNotifier(hub).OrderStatusUpdatedAsync(customerId, orderId, OrderStatus.Accepted, eta);

        hub.Clients.Received(1).User(customerId.ToString());
        await customerProxy.Received(1).SendCoreAsync(
            "OrderStatusUpdated",
            Arg.Is<object?[]>(args => args.Length == 3
                && Equals(args[0], orderId)
                && Equals(args[1], OrderStatus.Accepted)
                && Equals(args[2], eta)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderStatusUpdatedAsync_ShouldSendANullEta_WhenTheOrderHasNone()
    {
        var hub = Substitute.For<IHubContext<OrderHub>>();
        var customerProxy = Substitute.For<IClientProxy>();
        Guid customerId = Guid.NewGuid();
        hub.Clients.User(customerId.ToString()).Returns(customerProxy);

        await new OrderHubNotifier(hub).OrderStatusUpdatedAsync(customerId, Guid.NewGuid(), OrderStatus.Preparing, null);

        await customerProxy.Received(1).SendCoreAsync(
            "OrderStatusUpdated",
            Arg.Is<object?[]>(args => args.Length == 3 && args[2] == null),
            Arg.Any<CancellationToken>());
    }
}
