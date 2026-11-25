using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;
using Shared.Contracts.Messages;
using Shared.Contracts.Messaging;
using Shared.Messaging;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Messaging;

[Collection(RabbitMqCollection.Name)]
public class RabbitMqConsumerBaseTests(RabbitMqFixture rabbitMq)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public void QueueName_ShouldComeFromTheTopology()
    {
        RabbitMqConsumerBase<DishRated>.QueueName.ShouldBe(QueueNames.AdminDishRated);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleAndAckTheMessage_WhenTheHandlerSucceeds()
    {
        var handled = new HandledMessages();
        using IHost host = await StartHostAsync<SucceedingConsumer>(handled);
        DishRated message = CreateDishRated();

        await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(message);

        DishRated received = await handled.FirstAsync().WaitAsync(Timeout);
        received.MessageId.ShouldBe(message.MessageId);
        received.Stars.ShouldBe(4);
        await WaitUntilQueueIsEmptyAsync(host);

        await host.StopAsync();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNackWithoutRequeue_WhenTheHandlerThrows()
    {
        var handled = new HandledMessages();
        using IHost host = await StartHostAsync<FailingConsumer>(handled);

        await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(CreateDishRated());

        await handled.FirstAsync().WaitAsync(Timeout);
        await WaitUntilQueueIsEmptyAsync(host);
        await Task.Delay(TimeSpan.FromSeconds(1));
        handled.Count.ShouldBe(1);

        await host.StopAsync();
    }

    private async Task<IHost> StartHostAsync<TConsumer>(HandledMessages handled) where TConsumer : class, IHostedService
    {
        using (IHost setup = rabbitMq.CreateHost())
        {
            await setup.StartAsync();
            await using IChannel channel = await setup.Services.GetRequiredService<RabbitMqConnection>().CreateChannelAsync();
            await channel.QueuePurgeAsync(QueueNames.AdminDishRated);
            await setup.StopAsync();
        }

        IHost host = rabbitMq.CreateHost(services =>
        {
            services.AddSingleton(handled);
            services.AddSingleton(Substitute.For<IProcessedMessageStore>());
            services.AddHostedService<TConsumer>();
        });
        await host.StartAsync();
        return host;
    }

    private static async Task WaitUntilQueueIsEmptyAsync(IHost host)
    {
        await using IChannel channel = await host.Services.GetRequiredService<RabbitMqConnection>().CreateChannelAsync();
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            QueueDeclareOk queue = await channel.QueueDeclarePassiveAsync(QueueNames.AdminDishRated);
            if (queue.MessageCount == 0 && queue.ConsumerCount == 1)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{QueueNames.AdminDishRated} still has {queue.MessageCount} messages.");
            }

            await Task.Delay(200);
        }
    }

    private static DishRated CreateDishRated() => new()
    {
        RatingId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        MenuItemId = Guid.NewGuid(),
        CustomerName = "Test Customer",
        Stars = 4,
        RatedAt = DateTime.UtcNow
    };

    public sealed class HandledMessages
    {
        private readonly TaskCompletionSource<DishRated> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public int Count => _count;

        public Task<DishRated> FirstAsync() => _first.Task;

        public void Record(DishRated message)
        {
            Interlocked.Increment(ref _count);
            _first.TrySetResult(message);
        }
    }

    public sealed class SucceedingConsumer(RabbitMqConnection connection, IServiceScopeFactory scopeFactory, ILogger<SucceedingConsumer> logger)
        : RabbitMqConsumerBase<DishRated>(connection, scopeFactory, logger)
    {
        public override Task HandleAsync(DishRated message, IServiceProvider services, CancellationToken ct)
        {
            services.GetRequiredService<HandledMessages>().Record(message);
            return Task.CompletedTask;
        }
    }

    public sealed class FailingConsumer(RabbitMqConnection connection, IServiceScopeFactory scopeFactory, ILogger<FailingConsumer> logger)
        : RabbitMqConsumerBase<DishRated>(connection, scopeFactory, logger)
    {
        public override Task HandleAsync(DishRated message, IServiceProvider services, CancellationToken ct)
        {
            services.GetRequiredService<HandledMessages>().Record(message);
            throw new InvalidOperationException("Simulated handler failure.");
        }
    }
}
