using Admin.Web;
using Admin.Web.Data;
using Admin.Web.Messaging.Consumers;
using Admin.Web.Models.Entities;
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

namespace FoodOrdering.IntegrationTests.Admin;

[Collection(PostgresCollection.Name)]
public class OrderPlacedConsumerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task HandleAsync_ShouldCreateTheOrderWithItsItemsExtrasAndOnePlacedStatusHistoryRow()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid burgerId, Guid friesId) = await SeedMenuAsync(connectionString);
        OrderPlaced message = CreateMessage(burgerId, friesId);

        await HandleAsync(connectionString, message);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        Order order = await context.Orders
            .Include(o => o.Items).ThenInclude(i => i.Extras)
            .Include(o => o.StatusHistory)
            .SingleAsync();

        order.Id.ShouldBe(message.OrderId);
        order.OrderNumber.ShouldBe("FO-1042");
        order.Status.ShouldBe(OrderStatus.Placed);
        order.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
        order.Total.ShouldBe(27.00m);
        order.PlacedAt.ShouldBe(message.PlacedAt);
        order.CreatedAt.ShouldNotBe(default);

        order.Items.Count.ShouldBe(2);
        OrderItem burger = order.Items.Single(i => i.MenuItemId == burgerId);
        burger.Quantity.ShouldBe(2);
        burger.UnitPrice.ShouldBe(9.50m);
        burger.Extras.Select(e => e.Name).ShouldBe(["Extra cheese", "Bacon"], ignoreOrder: true);
        order.Items.Single(i => i.MenuItemId == friesId).Extras.ShouldBeEmpty();

        OrderStatusHistory history = order.StatusHistory.ShouldHaveSingleItem();
        history.Status.ShouldBe(OrderStatus.Placed);
        history.ChangedAt.ShouldBe(message.PlacedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotCreateASecondOrder_WhenTheSameOrderArrivesTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid burgerId, Guid friesId) = await SeedMenuAsync(connectionString);
        OrderPlaced message = CreateMessage(burgerId, friesId);

        await HandleAsync(connectionString, message);
        await HandleAsync(connectionString, message);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Orders.CountAsync()).ShouldBe(1);
        (await context.OrderItems.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldCreateOneOrderAndAckBothCopies_WhenTheSameMessageArrivesTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid burgerId, Guid friesId) = await SeedMenuAsync(connectionString);
        OrderPlaced message = CreateMessage(burgerId, friesId);
        using IHost host = BuildHost(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 2), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Orders.CountAsync()).ShouldBe(1);
        (await context.OrderItems.CountAsync()).ShouldBe(2);
        (await context.ProcessedMessages.CountAsync()).ShouldBe(1);
        await channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldSaveAProcessedMessageRow_WhenTheOrderIsCreated()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid burgerId, Guid friesId) = await SeedMenuAsync(connectionString);
        OrderPlaced message = CreateMessage(burgerId, friesId);
        using IHost host = BuildHost(connectionString);
        DateTime before = DateTime.UtcNow;

        await ConsumerOf(host).OnReceivedAsync(Substitute.For<IChannel>(), ConsumerDeliveries.Create(message, 1), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        ProcessedMessage row = await context.ProcessedMessages.SingleAsync();
        row.MessageId.ShouldBe(message.MessageId);
        row.Type.ShouldBe(nameof(OrderPlaced));
        row.ProcessedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        (await context.Orders.SingleAsync()).Id.ShouldBe(message.OrderId);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldSaveNoProcessedMessageRowAndNack_WhenSavingTheOrderFails()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        OrderPlaced message = CreateMessage(Guid.NewGuid(), Guid.NewGuid());
        using IHost host = BuildHost(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Orders.CountAsync()).ShouldBe(0);
        (await context.ProcessedMessages.CountAsync()).ShouldBe(0);
        await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldNotCreateASecondOrder_WhenTheSameOrderArrivesWithANewMessageId()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid burgerId, Guid friesId) = await SeedMenuAsync(connectionString);
        OrderPlaced message = CreateMessage(burgerId, friesId);
        OrderPlaced resent = message with { MessageId = Guid.NewGuid() };
        using IHost host = BuildHost(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(resent, 2), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Orders.CountAsync()).ShouldBe(1);
        (await context.ProcessedMessages.SingleAsync()).MessageId.ShouldBe(message.MessageId);
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldAckAsADuplicate_WhenAnotherCopySavesTheSameMessageIdAtTheSameTime()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid burgerId, Guid friesId) = await SeedMenuAsync(connectionString);
        OrderPlaced message = CreateMessage(burgerId, friesId);
        using IHost host = BuildHost(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await using AdminDbContext firstCopy = postgres.NewDbContext<AdminDbContext>(connectionString);
        await using IDbContextTransaction transaction = await firstCopy.Database.BeginTransactionAsync();
        firstCopy.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = message.MessageId,
            Type = nameof(OrderPlaced),
            ProcessedAt = DateTime.UtcNow
        });
        await firstCopy.SaveChangesAsync();

        Task secondCopy = ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 2), CancellationToken.None);
        await WaitUntilASessionWaitsForALockAsync(connectionString);
        await transaction.CommitAsync();
        await secondCopy;

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.ProcessedMessages.CountAsync()).ShouldBe(1);
        (await context.Orders.CountAsync()).ShouldBe(0);
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldCreateOneOrder_WhenTwoCopiesAreHandledAtTheSameTime()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid burgerId, Guid friesId) = await SeedMenuAsync(connectionString);
        OrderPlaced message = CreateMessage(burgerId, friesId);
        using IHost host = BuildHost(connectionString);

        await using AsyncServiceScope firstCopy = host.Services.CreateAsyncScope();
        AdminDbContext firstContext = firstCopy.ServiceProvider.GetRequiredService<AdminDbContext>();
        await using IDbContextTransaction transaction = await firstContext.Database.BeginTransactionAsync();
        firstCopy.ServiceProvider.GetRequiredService<IProcessedMessageStore>().MarkProcessed(message.MessageId, nameof(OrderPlaced));
        await ConsumerOf(host).HandleAsync(message, firstCopy.ServiceProvider, CancellationToken.None);

        Task secondCopy = ConsumerOf(host).OnReceivedAsync(Substitute.For<IChannel>(), ConsumerDeliveries.Create(message, 2), CancellationToken.None);
        await WaitUntilASessionWaitsForALockAsync(connectionString);
        await transaction.CommitAsync();
        await secondCopy;

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Orders.CountAsync()).ShouldBe(1);
        (await context.OrderItems.CountAsync()).ShouldBe(2);
        (await context.ProcessedMessages.SingleAsync()).MessageId.ShouldBe(message.MessageId);
    }

    private static OrderPlacedConsumer ConsumerOf(IHost host) =>
        host.Services.GetServices<IHostedService>().OfType<OrderPlacedConsumer>().Single();

    private async Task WaitUntilASessionWaitsForALockAsync(string connectionString)
    {
        await using AdminDbContext monitor = postgres.NewDbContext<AdminDbContext>(connectionString);
        await ConsumerDeliveries.WaitUntilASessionWaitsForALockAsync(monitor);
    }

    private static async Task HandleAsync(string connectionString, OrderPlaced message)
    {
        using IHost host = BuildHost(connectionString);
        OrderPlacedConsumer consumer = host.Services.GetServices<IHostedService>().OfType<OrderPlacedConsumer>().Single();

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
        builder.Services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddProcessedMessages<AdminDbContext, ProcessedMessage>();
        builder.Services.AddAdminServices();
        return builder.Build();
    }

    private async Task<(Guid BurgerId, Guid FriesId)> SeedMenuAsync(string connectionString)
    {
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        var category = new Category { Id = Guid.NewGuid(), Name = "Burgers" };
        var burger = new MenuItem { Id = Guid.NewGuid(), CategoryId = category.Id, Name = "Chicken Burger", Price = 9.50m };
        var fries = new MenuItem { Id = Guid.NewGuid(), CategoryId = category.Id, Name = "Fries", Price = 3.00m };
        context.Categories.Add(category);
        context.MenuItems.AddRange(burger, fries);
        await context.SaveChangesAsync();
        return (burger.Id, fries.Id);
    }

    private static OrderPlaced CreateMessage(Guid burgerId, Guid friesId)
    {
        Guid orderId = Guid.NewGuid();
        return new OrderPlaced
        {
            CorrelationId = orderId,
            OrderId = orderId,
            OrderNumber = "FO-1042",
            CustomerId = Guid.NewGuid(),
            CustomerName = "Sara Haddad",
            CustomerPhone = "+961 70 123 456",
            DeliveryType = DeliveryType.Delivery,
            Address = "Hamra Street, Beirut",
            PaymentMethod = PaymentMethod.Online,
            Items =
            [
                new OrderItemDto
                {
                    MenuItemId = burgerId,
                    Name = "Chicken Burger",
                    UnitPrice = 9.50m,
                    Quantity = 2,
                    Extras = [new ExtraDto { Name = "Extra cheese", Price = 1.00m }, new ExtraDto { Name = "Bacon", Price = 1.50m }]
                },
                new OrderItemDto { MenuItemId = friesId, Name = "Fries", UnitPrice = 3.00m, Quantity = 1 }
            ],
            Total = 27.00m,
            PlacedAt = new DateTime(2026, 11, 24, 12, 30, 0, DateTimeKind.Utc)
        };
    }
}
