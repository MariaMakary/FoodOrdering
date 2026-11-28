using Admin.Web.Data;
using Admin.Web.Messaging.Consumers;
using Admin.Web.Models.Entities;
using Admin.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RabbitMQ.Client;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Admin;

[Collection(PostgresCollection.Name)]
public class PaymentCompletedConsumerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task HandleAsync_ShouldMarkTheOrderPaidAndSaveAPaymentRow()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString);
        PaymentCompleted message = Message(orderId);

        await AdminConsumerHost.HandleAsync<PaymentCompletedConsumer, PaymentCompleted>(connectionString, message);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        Order order = await context.Orders.Include(o => o.Payment).SingleAsync();
        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        order.Status.ShouldBe(OrderStatus.Placed);
        Payment payment = order.Payment.ShouldNotBeNull();
        payment.Id.ShouldBe(message.PaymentId);
        payment.Amount.ShouldBe(27.00m);
        payment.Method.ShouldBe(PaymentMethod.Online);
        payment.PaidAt.ShouldBe(message.PaidAt);
        payment.CreatedAt.ShouldNotBe(default);
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenTheOrderDoesNotExist()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();

        await Should.ThrowAsync<OrderNotReceivedException>(
            () => AdminConsumerHost.HandleAsync<PaymentCompletedConsumer, PaymentCompleted>(connectionString, Message(Guid.NewGuid())));

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Payments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldNackAndSaveNoProcessedMessageRow_WhenTheOrderDoesNotExistYet()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(Message(Guid.NewGuid()), 1), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Payments.CountAsync()).ShouldBe(0);
        (await context.ProcessedMessages.CountAsync()).ShouldBe(0);
        await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldRecordThePayment_WhenItIsDeliveredAgainAfterTheOrderArrived()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid menuItemId;
        await using (AdminDbContext seed = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            menuItemId = await AdminOrderSeed.AddMenuItemAsync(seed);
        }

        Guid orderId = Guid.NewGuid();
        PaymentCompleted payment = Message(orderId) with { Amount = 9.50m };
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(payment, 1), CancellationToken.None);
        await AdminConsumerHost.ConsumerOf<OrderPlacedConsumer>(host)
            .OnReceivedAsync(channel, ConsumerDeliveries.Create(AdminOrderSeed.OrderPlacedFor(orderId, menuItemId), 2), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(payment, 3), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        Order order = await context.Orders.Include(o => o.Payment).SingleAsync();
        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        order.Payment.ShouldNotBeNull().Id.ShouldBe(payment.PaymentId);
        await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.Received(1).BasicAckAsync(3, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldSaveOnePaymentAndAckBothCopies_WhenTheSameMessageArrivesTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString);
        PaymentCompleted message = Message(orderId);
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 2), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Payments.CountAsync()).ShouldBe(1);
        ProcessedMessage row = await context.ProcessedMessages.SingleAsync();
        row.MessageId.ShouldBe(message.MessageId);
        row.Type.ShouldBe(nameof(PaymentCompleted));
        await channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldNotSaveASecondPayment_WhenTheSamePaymentArrivesWithANewMessageId()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString);
        PaymentCompleted message = Message(orderId);
        PaymentCompleted resent = message with { MessageId = Guid.NewGuid() };
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(resent, 2), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Payments.SingleAsync()).Id.ShouldBe(message.PaymentId);
        (await context.ProcessedMessages.SingleAsync()).MessageId.ShouldBe(message.MessageId);
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    private static PaymentCompletedConsumer ConsumerOf(IHost host) =>
        AdminConsumerHost.ConsumerOf<PaymentCompletedConsumer>(host);

    private async Task<Guid> SeedOrderAsync(string connectionString)
    {
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        return (await AdminOrderSeed.AddOrderAsync(context)).Id;
    }

    private static PaymentCompleted Message(Guid orderId) => new()
    {
        CorrelationId = orderId,
        OrderId = orderId,
        PaymentId = Guid.NewGuid(),
        Amount = 27.00m,
        Method = PaymentMethod.Online,
        PaidAt = AdminOrderSeed.PlacedAt.AddSeconds(18)
    };
}
