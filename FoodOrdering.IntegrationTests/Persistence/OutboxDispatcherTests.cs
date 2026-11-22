using Client.Web.Data;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.Contracts.Messages;
using Shared.Messaging;
using Shared.Persistence.Outbox;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public class OutboxDispatcherTests(PostgresFixture postgres)
{
    private readonly IMessagePublisher _publisher = Substitute.For<IMessagePublisher>();

    [Fact]
    public async Task DispatchPendingAsync_ShouldPublishTheRowAndSetSentAt()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        OrderCancelled message = NewMessage(DateTime.UtcNow);
        await SaveAsync(connectionString, message);

        int sent = await NewDispatcher(connectionString).DispatchPendingAsync();

        sent.ShouldBe(1);
        await _publisher.Received(1).PublishAsync(Arg.Is<MessageBase>(m => m.MessageId == message.MessageId), Arg.Any<CancellationToken>());
        OutboxMessage row = await LoadAsync(connectionString, message.MessageId);
        row.SentAt.ShouldNotBeNull();
        row.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task DispatchPendingAsync_ShouldKeepSentAtNullAndCountTheAttempt_WhenPublishFails()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        OrderCancelled message = NewMessage(DateTime.UtcNow);
        await SaveAsync(connectionString, message);
        _publisher.PublishAsync(Arg.Any<MessageBase>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("RabbitMQ is down"));

        int sent = await NewDispatcher(connectionString).DispatchPendingAsync();

        sent.ShouldBe(0);
        OutboxMessage row = await LoadAsync(connectionString, message.MessageId);
        row.SentAt.ShouldBeNull();
        row.Attempts.ShouldBe(1);
        row.LastError.ShouldBe("RabbitMQ is down");
    }

    [Fact]
    public async Task DispatchPendingAsync_ShouldStopAtTheFirstFailure_SoLaterRowsKeepTheirOrder()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        OrderCancelled first = NewMessage(DateTime.UtcNow.AddSeconds(-10));
        OrderCancelled second = NewMessage(DateTime.UtcNow);
        await SaveAsync(connectionString, second, first);
        _publisher.PublishAsync(Arg.Is<MessageBase>(m => m.MessageId == first.MessageId), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("RabbitMQ is down"));

        await NewDispatcher(connectionString).DispatchPendingAsync();

        await _publisher.DidNotReceive().PublishAsync(Arg.Is<MessageBase>(m => m.MessageId == second.MessageId), Arg.Any<CancellationToken>());
        (await LoadAsync(connectionString, second.MessageId)).SentAt.ShouldBeNull();
    }

    [Fact]
    public async Task DispatchPendingAsync_ShouldPublishTheOldestRowFirst()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        OrderCancelled older = NewMessage(DateTime.UtcNow.AddSeconds(-10));
        OrderCancelled newer = NewMessage(DateTime.UtcNow);
        await SaveAsync(connectionString, newer, older);
        var published = new List<Guid>();
        _publisher.PublishAsync(Arg.Do<MessageBase>(m => published.Add(m.MessageId)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await NewDispatcher(connectionString).DispatchPendingAsync();

        published.ShouldBe([older.MessageId, newer.MessageId]);
    }

    [Fact]
    public async Task DispatchPendingAsync_ShouldSkipRowsThatWereAlreadySent()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await SaveAsync(connectionString, NewMessage(DateTime.UtcNow));
        OutboxDispatcher<ClientDbContext> dispatcher = NewDispatcher(connectionString);
        await dispatcher.DispatchPendingAsync();

        int sentAgain = await dispatcher.DispatchPendingAsync();

        sentAgain.ShouldBe(0);
        await _publisher.Received(1).PublishAsync(Arg.Any<MessageBase>(), Arg.Any<CancellationToken>());
    }

    private OutboxDispatcher<ClientDbContext> NewDispatcher(string connectionString)
    {
        ServiceProvider services = new ServiceCollection()
            .AddDbContext<ClientDbContext>(options => options.UseNpgsql(connectionString))
            .AddSingleton(_publisher)
            .BuildServiceProvider();

        return new OutboxDispatcher<ClientDbContext>(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxDispatcher<ClientDbContext>>.Instance);
    }

    private async Task SaveAsync(string connectionString, params OrderCancelled[] messages)
    {
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        var outbox = new EfOutbox<ClientDbContext>(context);
        foreach (OrderCancelled message in messages)
        {
            outbox.Add(message);
        }
        await context.SaveChangesAsync();
    }

    private async Task<OutboxMessage> LoadAsync(string connectionString, Guid messageId)
    {
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        return await context.Set<OutboxMessage>().SingleAsync(m => m.Id == messageId);
    }

    private static OrderCancelled NewMessage(DateTime createdAt)
    {
        Guid orderId = Guid.NewGuid();
        return new OrderCancelled
        {
            OrderId = orderId,
            CorrelationId = orderId,
            Reason = "Changed my mind",
            CancelledAt = createdAt,
            CreatedAt = createdAt
        };
    }
}
