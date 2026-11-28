using Admin.Web.Data;
using Admin.Web.Messaging.Consumers;
using Admin.Web.Models.Entities;
using Admin.Web.Repositories;
using Admin.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RabbitMQ.Client;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Admin;

[Collection(PostgresCollection.Name)]
public class OrderCancelledConsumerTests(PostgresFixture postgres)
{
    private static readonly DateTime AcceptedAt = AdminOrderSeed.PlacedAt.AddMinutes(2);
    private static readonly DateTime Eta = AdminOrderSeed.PlacedAt.AddMinutes(45);

    [Fact]
    public async Task HandleAsync_ShouldCancelAPlacedOrderAndAddACancelledStatusHistoryRow()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed);
        OrderCancelled message = Message(orderId);

        await AdminConsumerHost.HandleAsync<OrderCancelledConsumer, OrderCancelled>(connectionString, message);

        Order order = await LoadOrderAsync(connectionString);
        order.Status.ShouldBe(OrderStatus.Cancelled);
        OrderStatusHistory cancelled = order.StatusHistory.Single(h => h.Status == OrderStatus.Cancelled);
        cancelled.ChangedAt.ShouldBe(message.CancelledAt);
        cancelled.ChangedByUserId.ShouldBeNull();
        cancelled.Note.ShouldBe("Cancelled by customer");
        (await OutboxRowsAsync(connectionString)).ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ShouldLeaveTheOrderAcceptedAndSaveOneOrderStatusChangedInTheOutbox_WhenTheOrderIsAlreadyAccepted()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Accepted);

        await AdminConsumerHost.HandleAsync<OrderCancelledConsumer, OrderCancelled>(connectionString, Message(orderId));

        Order order = await LoadOrderAsync(connectionString);
        order.Status.ShouldBe(OrderStatus.Accepted);
        order.StatusHistory.ShouldNotContain(h => h.Status == OrderStatus.Cancelled);
        OutboxMessage row = (await OutboxRowsAsync(connectionString)).ShouldHaveSingleItem();
        row.Type.ShouldBe(nameof(OrderStatusChanged));
        row.CorrelationId.ShouldBe(orderId);
        OrderStatusChanged sent = row.ToMessage().ShouldBeOfType<OrderStatusChanged>();
        sent.OrderId.ShouldBe(orderId);
        sent.NewStatus.ShouldBe(OrderStatus.Accepted);
        sent.EstimatedDeliveryTime.ShouldBe(Eta);
        sent.ChangedAt.ShouldBe(AcceptedAt);
        sent.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_ShouldSendTheRejectionAgain_WhenTheOrderIsAlreadyRejected()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId;
        await using (AdminDbContext seed = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            orderId = (await AdminOrderSeed.AddOrderAsync(seed, OrderStatus.Rejected, AcceptedAt, statusNote: "Out of chicken")).Id;
        }

        await AdminConsumerHost.HandleAsync<OrderCancelledConsumer, OrderCancelled>(connectionString, Message(orderId));

        (await LoadOrderAsync(connectionString)).Status.ShouldBe(OrderStatus.Rejected);
        OrderStatusChanged sent = (await OutboxRowsAsync(connectionString)).ShouldHaveSingleItem().ToMessage().ShouldBeOfType<OrderStatusChanged>();
        sent.NewStatus.ShouldBe(OrderStatus.Rejected);
        sent.Reason.ShouldBe("Out of chicken");
    }

    [Fact]
    public async Task HandleAsync_ShouldChangeNothing_WhenTheOrderIsAlreadyCancelled()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Cancelled);

        await AdminConsumerHost.HandleAsync<OrderCancelledConsumer, OrderCancelled>(connectionString, Message(orderId));

        Order order = await LoadOrderAsync(connectionString);
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.StatusHistory.Count(h => h.Status == OrderStatus.Cancelled).ShouldBe(1);
        (await OutboxRowsAsync(connectionString)).ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenTheOrderDoesNotExist()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();

        await Should.ThrowAsync<OrderNotReceivedException>(
            () => AdminConsumerHost.HandleAsync<OrderCancelledConsumer, OrderCancelled>(connectionString, Message(Guid.NewGuid())));
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowAConcurrencyError_WhenStaffChangeTheOrderBeforeTheCancelIsSaved()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed);
        using IHost host = AdminConsumerHost.Build(connectionString);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOrderRepository>().GetOrderAsync(orderId);

        await using (AdminDbContext staff = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            Order accepted = await staff.Orders.SingleAsync();
            accepted.Status = OrderStatus.Accepted;
            await staff.SaveChangesAsync();
        }

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => AdminConsumerHost.ConsumerOf<OrderCancelledConsumer>(host).HandleAsync(Message(orderId), scope.ServiceProvider, CancellationToken.None));

        (await LoadOrderAsync(connectionString)).Status.ShouldBe(OrderStatus.Accepted);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldNackAndSaveNoProcessedMessageRow_WhenTheOrderDoesNotExistYet()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(Message(Guid.NewGuid()), 1), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.ProcessedMessages.CountAsync()).ShouldBe(0);
        await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldCancelTheOrder_WhenTheCancelIsDeliveredAgainAfterTheOrderArrived()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid menuItemId;
        await using (AdminDbContext seed = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            menuItemId = await AdminOrderSeed.AddMenuItemAsync(seed);
        }

        Guid orderId = Guid.NewGuid();
        OrderCancelled cancel = Message(orderId);
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(cancel, 1), CancellationToken.None);
        await AdminConsumerHost.ConsumerOf<OrderPlacedConsumer>(host)
            .OnReceivedAsync(channel, ConsumerDeliveries.Create(AdminOrderSeed.OrderPlacedFor(orderId, menuItemId), 2), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(cancel, 3), CancellationToken.None);

        Order order = await LoadOrderAsync(connectionString);
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.StatusHistory.Select(h => h.Status).ShouldBe([OrderStatus.Placed, OrderStatus.Cancelled], ignoreOrder: true);
        await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
        await channel.Received(1).BasicAckAsync(3, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldSendTheAcceptedStatus_WhenTheCancelIsDeliveredAgainAfterAConcurrencyError()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed);
        OrderCancelled cancel = Message(orderId);
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOrderRepository>().GetOrderAsync(orderId);
            await AcceptAsync(connectionString);
            await Should.ThrowAsync<DbUpdateConcurrencyException>(
                () => ConsumerOf(host).HandleAsync(cancel, scope.ServiceProvider, CancellationToken.None));
        }

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(cancel, 1), CancellationToken.None);

        (await LoadOrderAsync(connectionString)).Status.ShouldBe(OrderStatus.Accepted);
        OrderStatusChanged sent = (await OutboxRowsAsync(connectionString)).ShouldHaveSingleItem().ToMessage().ShouldBeOfType<OrderStatusChanged>();
        sent.NewStatus.ShouldBe(OrderStatus.Accepted);
        await channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldCancelOnceAndAckBothCopies_WhenTheSameMessageArrivesTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed);
        OrderCancelled message = Message(orderId);
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 2), CancellationToken.None);

        Order order = await LoadOrderAsync(connectionString);
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.StatusHistory.Count(h => h.Status == OrderStatus.Cancelled).ShouldBe(1);
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        ProcessedMessage row = await context.ProcessedMessages.SingleAsync();
        row.MessageId.ShouldBe(message.MessageId);
        row.Type.ShouldBe(nameof(OrderCancelled));
        await channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldSendTheCurrentStatusOnce_WhenTheSameLateCancelArrivesTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Accepted);
        OrderCancelled message = Message(orderId);
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 2), CancellationToken.None);

        (await OutboxRowsAsync(connectionString)).ShouldHaveSingleItem().Type.ShouldBe(nameof(OrderStatusChanged));
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
    }

    private static OrderCancelledConsumer ConsumerOf(IHost host) =>
        AdminConsumerHost.ConsumerOf<OrderCancelledConsumer>(host);

    private async Task AcceptAsync(string connectionString)
    {
        await using AdminDbContext staff = postgres.NewDbContext<AdminDbContext>(connectionString);
        Order order = await staff.Orders.SingleAsync();
        order.Status = OrderStatus.Accepted;
        order.EstimatedDeliveryTime = Eta;
        staff.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Status = OrderStatus.Accepted,
            ChangedAt = AcceptedAt
        });
        await staff.SaveChangesAsync();
    }

    private async Task<Guid> SeedOrderAsync(string connectionString, OrderStatus status)
    {
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        DateTime? eta = status == OrderStatus.Placed ? null : Eta;
        return (await AdminOrderSeed.AddOrderAsync(context, status, AcceptedAt, eta)).Id;
    }

    private async Task<Order> LoadOrderAsync(string connectionString)
    {
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        return await context.Orders.Include(o => o.StatusHistory).SingleAsync();
    }

    private async Task<List<OutboxMessage>> OutboxRowsAsync(string connectionString)
    {
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        return await context.Set<OutboxMessage>().ToListAsync();
    }

    private static OrderCancelled Message(Guid orderId) => new()
    {
        CorrelationId = orderId,
        OrderId = orderId,
        Reason = "Cancelled by customer",
        CancelledAt = AdminOrderSeed.PlacedAt.AddMinutes(3)
    };
}
