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
public class DishRatedConsumerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task HandleAsync_ShouldSaveTheRatingWithTheMessageRatingIdAndRatedAt()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid orderId, Guid menuItemId) = await SeedDeliveredOrderAsync(connectionString);
        DishRated message = Message(orderId, menuItemId);

        await AdminConsumerHost.HandleAsync<DishRatedConsumer, DishRated>(connectionString, message);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        Rating rating = await context.Ratings.SingleAsync();
        rating.Id.ShouldBe(message.RatingId);
        rating.OrderId.ShouldBe(orderId);
        rating.MenuItemId.ShouldBe(menuItemId);
        rating.CustomerName.ShouldBe("Sara Haddad");
        rating.Stars.ShouldBe(5);
        rating.Comment.ShouldBe("Best burger in town!");
        rating.RatedAt.ShouldBe(message.RatedAt);
        rating.CreatedAt.ShouldNotBe(default);
        rating.CreatedAt.ShouldNotBe(message.RatedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldThrow_WhenTheOrderDoesNotExist()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();

        await Should.ThrowAsync<OrderNotReceivedException>(
            () => AdminConsumerHost.HandleAsync<DishRatedConsumer, DishRated>(connectionString, Message(Guid.NewGuid(), Guid.NewGuid())));
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldNackAndSaveNothing_WhenTheOrderDoesNotExistYet()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(Message(Guid.NewGuid(), Guid.NewGuid()), 1), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Ratings.CountAsync()).ShouldBe(0);
        (await context.ProcessedMessages.CountAsync()).ShouldBe(0);
        await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldSaveTheRating_WhenItIsDeliveredAgainAfterTheOrderArrived()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        Guid menuItemId;
        await using (AdminDbContext seed = postgres.NewDbContext<AdminDbContext>(connectionString))
        {
            menuItemId = await AdminOrderSeed.AddMenuItemAsync(seed);
        }

        Guid orderId = Guid.NewGuid();
        DishRated rating = Message(orderId, menuItemId);
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(rating, 1), CancellationToken.None);
        await AdminConsumerHost.ConsumerOf<OrderPlacedConsumer>(host)
            .OnReceivedAsync(channel, ConsumerDeliveries.Create(AdminOrderSeed.OrderPlacedFor(orderId, menuItemId), 2), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(rating, 3), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Ratings.SingleAsync()).Id.ShouldBe(rating.RatingId);
        await channel.Received(1).BasicNackAsync(1, false, false, Arg.Any<CancellationToken>());
        await channel.Received(1).BasicAckAsync(3, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldSaveOneRatingAndAckBothCopies_WhenTheSameMessageArrivesTwice()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid orderId, Guid menuItemId) = await SeedDeliveredOrderAsync(connectionString);
        DishRated message = Message(orderId, menuItemId);
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 2), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Ratings.CountAsync()).ShouldBe(1);
        ProcessedMessage row = await context.ProcessedMessages.SingleAsync();
        row.MessageId.ShouldBe(message.MessageId);
        row.Type.ShouldBe(nameof(DishRated));
        await channel.Received(1).BasicAckAsync(1, false, Arg.Any<CancellationToken>());
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldNotSaveASecondRating_WhenTheSameRatingArrivesWithANewMessageId()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid orderId, Guid menuItemId) = await SeedDeliveredOrderAsync(connectionString);
        DishRated message = Message(orderId, menuItemId);
        DishRated resent = message with { MessageId = Guid.NewGuid() };
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(message, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(resent, 2), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        (await context.Ratings.SingleAsync()).Id.ShouldBe(message.RatingId);
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldKeepTheFirstRatingAndAck_WhenTheSameDishOfTheOrderIsRatedAgain()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<AdminDbContext>();
        (Guid orderId, Guid menuItemId) = await SeedDeliveredOrderAsync(connectionString);
        DishRated first = Message(orderId, menuItemId);
        DishRated second = Message(orderId, menuItemId) with { Stars = 1, Comment = "Changed my mind" };
        using IHost host = AdminConsumerHost.Build(connectionString);
        IChannel channel = Substitute.For<IChannel>();

        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(first, 1), CancellationToken.None);
        await ConsumerOf(host).OnReceivedAsync(channel, ConsumerDeliveries.Create(second, 2), CancellationToken.None);

        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        Rating rating = await context.Ratings.SingleAsync();
        rating.Id.ShouldBe(first.RatingId);
        rating.Stars.ShouldBe(5);
        await channel.Received(1).BasicAckAsync(2, false, Arg.Any<CancellationToken>());
        await channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    private static DishRatedConsumer ConsumerOf(IHost host) =>
        AdminConsumerHost.ConsumerOf<DishRatedConsumer>(host);

    private async Task<(Guid OrderId, Guid MenuItemId)> SeedDeliveredOrderAsync(string connectionString)
    {
        await using AdminDbContext context = postgres.NewDbContext<AdminDbContext>(connectionString);
        Guid menuItemId = await AdminOrderSeed.AddMenuItemAsync(context);
        Order order = await AdminOrderSeed.AddOrderAsync(context, OrderStatus.Delivered);
        return (order.Id, menuItemId);
    }

    private static DishRated Message(Guid orderId, Guid menuItemId) => new()
    {
        CorrelationId = orderId,
        RatingId = Guid.NewGuid(),
        OrderId = orderId,
        MenuItemId = menuItemId,
        CustomerName = "Sara Haddad",
        Stars = 5,
        Comment = "Best burger in town!",
        RatedAt = new DateTime(2026, 11, 24, 14, 10, 0, DateTimeKind.Utc)
    };
}
