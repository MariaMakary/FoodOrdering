using Client.Web;
using Client.Web.Data;
using Client.Web.Messaging.Consumers;
using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Client.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RabbitMQ.Client;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Messaging;
using Shared.Persistence.ProcessedMessages;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class OrderStatusChangedConsumerTests(PostgresFixture postgres)
{
    private static readonly DateTime PlacedAt = new(2026, 11, 24, 12, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_ShouldUpdateTheStatusAndEtaAndAddAStatusHistoryRow()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = Accepted(orderId, PlacedAt.AddMinutes(2));

        await HandleAsync(connectionString, message);

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Accepted);
        order.EstimatedDeliveryTime.ShouldBe(message.EstimatedDeliveryTime);
        order.LastStatusChangedAt.ShouldBe(message.ChangedAt);
        OrderStatusHistory history = order.StatusHistory.ShouldHaveSingleItem();
        history.Status.ShouldBe(OrderStatus.Accepted);
        history.ChangedAt.ShouldBe(message.ChangedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldSaveTheRejectionReason_WhenTheOrderIsRejected()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = new()
        {
            OrderId = orderId,
            NewStatus = OrderStatus.Rejected,
            Reason = "Sorry, we are out of chicken today.",
            ChangedAt = PlacedAt.AddMinutes(2)
        };

        await HandleAsync(connectionString, message);

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Rejected);
        order.RejectionReason.ShouldBe("Sorry, we are out of chicken today.");
        order.StatusHistory.ShouldHaveSingleItem().Note.ShouldBe("Sorry, we are out of chicken today.");
    }

    [Fact]
    public async Task HandleAsync_ShouldNotThrow_WhenTheOrderDoesNotExist()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();

        await Should.NotThrowAsync(() => HandleAsync(connectionString, Accepted(Guid.NewGuid(), PlacedAt)));

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await context.OrderStatusHistory.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_ShouldChangeNothing_WhenTheMessageIsOlderThanTheLastAppliedChange()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed, PlacedAt);
        OrderStatusChanged preparing = new() { OrderId = orderId, NewStatus = OrderStatus.Preparing, ChangedAt = PlacedAt.AddMinutes(5) };
        await HandleAsync(connectionString, preparing);

        await HandleAsync(connectionString, Accepted(orderId, PlacedAt.AddMinutes(2)));

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Preparing);
        order.EstimatedDeliveryTime.ShouldBeNull();
        order.LastStatusChangedAt.ShouldBe(preparing.ChangedAt);
        order.StatusHistory.ShouldHaveSingleItem().Status.ShouldBe(OrderStatus.Preparing);
    }

    [Fact]
    public async Task HandleAsync_ShouldChangeNothing_WhenTheSameMessageArrivesTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = Accepted(orderId, PlacedAt.AddMinutes(2));

        await HandleAsync(connectionString, message);
        await HandleAsync(connectionString, message);

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Accepted);
        order.StatusHistory.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task HandleAsync_ShouldSetAccepted_WhenTheClientShowsCancelled()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Cancelled, PlacedAt.AddMinutes(1));
        OrderStatusChanged message = Accepted(orderId, PlacedAt.AddMinutes(2));

        await HandleAsync(connectionString, message);

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Accepted);
        order.EstimatedDeliveryTime.ShouldBe(message.EstimatedDeliveryTime);
        order.LastStatusChangedAt.ShouldBe(message.ChangedAt);
        OrderStatusHistory history = order.StatusHistory.ShouldHaveSingleItem();
        history.Status.ShouldBe(OrderStatus.Accepted);
        history.Note.ShouldBe(OrderStatusService.CancelRefusedNote);
    }

    [Fact]
    public async Task HandleAsync_ShouldSetAcceptedAndApplyTheNextStatus_WhenTheClientCancelledAfterTheAdminAccepted()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Cancelled, PlacedAt.AddMinutes(3));
        OrderStatusChanged accepted = Accepted(orderId, PlacedAt.AddMinutes(2));
        OrderStatusChanged preparing = new() { OrderId = orderId, NewStatus = OrderStatus.Preparing, ChangedAt = PlacedAt.AddMinutes(2.5) };

        await HandleAsync(connectionString, accepted);
        await HandleAsync(connectionString, accepted);
        await HandleAsync(connectionString, preparing);

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Preparing);
        order.EstimatedDeliveryTime.ShouldBe(accepted.EstimatedDeliveryTime);
        order.LastStatusChangedAt.ShouldBe(preparing.ChangedAt);
        order.StatusHistory.Select(h => h.Status).ShouldBe([OrderStatus.Accepted, OrderStatus.Preparing], ignoreOrder: true);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldApplyTheChangeOnceAndAckBothCopies_WhenTheSameMessageArrivesTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = Accepted(orderId, PlacedAt.AddMinutes(2));
        using IHost host = BuildHost(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 2), CancellationToken.None);

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Accepted);
        order.StatusHistory.ShouldHaveSingleItem();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await context.ProcessedMessages.CountAsync()).ShouldBe(1);
        await channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldSaveAProcessedMessageRow_WhenTheChangeIsApplied()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = Accepted(orderId, PlacedAt.AddMinutes(2));
        using IHost host = BuildHost(connectionString);
        DateTime before = DateTime.UtcNow;

        await ConsumerOf(host).OnReceivedAsync(Substitute.For<IChannel>(), ConsumerDeliveries.Create(message, 1), CancellationToken.None);

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        ProcessedMessage row = await context.ProcessedMessages.SingleAsync();
        row.MessageId.ShouldBe(message.MessageId);
        row.Type.ShouldBe(nameof(OrderStatusChanged));
        row.ProcessedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldSaveNoProcessedMessageRowAndNack_WhenSavingTheChangeFails()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = new()
        {
            OrderId = orderId,
            NewStatus = OrderStatus.Rejected,
            Reason = new string('x', 501),
            ChangedAt = PlacedAt.AddMinutes(2)
        };
        using IHost host = BuildHost(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Placed);
        order.StatusHistory.ShouldBeEmpty();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await context.ProcessedMessages.CountAsync()).ShouldBe(0);
        await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldAckAndSaveNoProcessedMessageRow_WhenTheOrderDoesNotExist()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        OrderStatusChanged message = Accepted(Guid.NewGuid(), PlacedAt);
        using IHost host = BuildHost(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await context.ProcessedMessages.CountAsync()).ShouldBe(0);
        await channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldAckAsADuplicate_WhenAnotherCopySavesTheSameMessageIdAtTheSameTime()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = Accepted(orderId, PlacedAt.AddMinutes(2));
        using IHost host = BuildHost(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await using ClientDbContext firstCopy = postgres.NewDbContext<ClientDbContext>(connectionString);
        await using IDbContextTransaction transaction = await firstCopy.Database.BeginTransactionAsync();
        firstCopy.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = message.MessageId,
            Type = nameof(OrderStatusChanged),
            ProcessedAt = DateTime.UtcNow
        });
        await firstCopy.SaveChangesAsync();

        Task secondCopy = ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 2), CancellationToken.None);
        await WaitUntilASessionWaitsForALockAsync(connectionString);
        await transaction.CommitAsync();
        await secondCopy;

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Placed);
        order.StatusHistory.ShouldBeEmpty();
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldApplyTheChangeOnce_WhenTwoCopiesAreHandledAtTheSameTime()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        Guid orderId = await SeedOrderAsync(connectionString, OrderStatus.Placed, PlacedAt);
        OrderStatusChanged message = Accepted(orderId, PlacedAt.AddMinutes(2));
        using IHost host = BuildHost(connectionString);

        await using AsyncServiceScope firstCopy = host.Services.CreateAsyncScope();
        ClientDbContext firstContext = firstCopy.ServiceProvider.GetRequiredService<ClientDbContext>();
        await using IDbContextTransaction transaction = await firstContext.Database.BeginTransactionAsync();
        firstCopy.ServiceProvider.GetRequiredService<IProcessedMessageStore>().MarkProcessed(message.MessageId, nameof(OrderStatusChanged));
        await ConsumerOf(host).HandleAsync(message, firstCopy.ServiceProvider, CancellationToken.None);

        Task secondCopy = ConsumerOf(host).OnReceivedAsync(Substitute.For<IChannel>(), ConsumerDeliveries.Create(message, 2), CancellationToken.None);
        await WaitUntilASessionWaitsForALockAsync(connectionString);
        await transaction.CommitAsync();
        await secondCopy;

        Order order = await LoadOrderAsync(connectionString, orderId);
        order.Status.ShouldBe(OrderStatus.Accepted);
        order.StatusHistory.ShouldHaveSingleItem();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await context.ProcessedMessages.SingleAsync()).MessageId.ShouldBe(message.MessageId);
    }

    private static OrderStatusChangedConsumer ConsumerOf(IHost host) =>
        host.Services.GetServices<IHostedService>().OfType<OrderStatusChangedConsumer>().Single();

    private async Task WaitUntilASessionWaitsForALockAsync(string connectionString)
    {
        await using ClientDbContext monitor = postgres.NewDbContext<ClientDbContext>(connectionString);
        await ConsumerDeliveries.WaitUntilASessionWaitsForALockAsync(monitor);
    }

    private static OrderStatusChanged Accepted(Guid orderId, DateTime changedAt) => new()
    {
        CorrelationId = orderId,
        OrderId = orderId,
        NewStatus = OrderStatus.Accepted,
        EstimatedDeliveryTime = changedAt.AddMinutes(45),
        ChangedAt = changedAt
    };

    private static async Task HandleAsync(string connectionString, OrderStatusChanged message)
    {
        using IHost host = BuildHost(connectionString);
        OrderStatusChangedConsumer consumer = host.Services.GetServices<IHostedService>().OfType<OrderStatusChangedConsumer>().Single();

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        await consumer.HandleAsync(message, scope.ServiceProvider, CancellationToken.None);
    }

    private static IHost BuildHost(string connectionString)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "localhost",
            ["RabbitMq:Username"] = "unused",
            ["RabbitMq:Password"] = "unused",
            ["RabbitMq:ExchangeName"] = "food.events"
        });
        builder.Services.AddRabbitMq(builder.Configuration);
        builder.Services.AddDbContext<ClientDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddProcessedMessages<ClientDbContext, ProcessedMessage>();
        builder.Services.AddClientServices();
        return builder.Build();
    }

    private async Task<Order> LoadOrderAsync(string connectionString, Guid orderId)
    {
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        return await context.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Id == orderId);
    }

    private async Task<Guid> SeedOrderAsync(string connectionString, OrderStatus status, DateTime lastStatusChangedAt)
    {
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        var customer = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "sara@example.com",
            Email = "sara@example.com",
            FirstName = "Sara",
            LastName = "Haddad"
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "FO-1042",
            CustomerId = customer.Id,
            DeliveryType = DeliveryType.Delivery,
            Address = "Hamra Street, Beirut",
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.CashOnDelivery,
            Status = status,
            Total = 27.00m,
            LastStatusChangedAt = lastStatusChangedAt
        };
        context.Users.Add(customer);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
