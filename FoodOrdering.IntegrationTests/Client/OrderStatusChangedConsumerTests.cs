using Client.Web;
using Client.Web.Data;
using Client.Web.Messaging.Consumers;
using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Client.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Messaging;
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
