using System.Text.Json;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Contracts.Messaging;
using Shared.Contracts.Serialization;
using Shared.Messaging;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Messaging;

[Collection(RabbitMqCollection.Name)]
public class RabbitMqPublisherTests(RabbitMqFixture rabbitMq)
{
    [Fact]
    public async Task PublishAsync_ShouldPutExactlyOneMessageInItsQueue()
    {
        using IHost host = await StartHostAsync();
        await using IChannel channel = await OpenChannelAsync(host);
        await channel.QueuePurgeAsync(QueueNames.AdminOrderPlaced);
        OrderPlaced message = CreateOrderPlaced();

        await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(message);

        QueueDeclareOk queue = await channel.QueueDeclarePassiveAsync(QueueNames.AdminOrderPlaced);
        queue.MessageCount.ShouldBe(1u);
        BasicGetResult received = (await channel.BasicGetAsync(QueueNames.AdminOrderPlaced, autoAck: true)).ShouldNotBeNull();
        received.BasicProperties.MessageId.ShouldBe(message.MessageId.ToString());
        received.BasicProperties.Type.ShouldBe(nameof(OrderPlaced));

        await host.StopAsync();
    }

    [Fact]
    public async Task PublishAsync_ShouldSendAPersistentJsonMessageWithAllItsProperties()
    {
        using IHost host = await StartHostAsync();
        await using IChannel channel = await OpenChannelAsync(host);
        await channel.QueuePurgeAsync(QueueNames.AdminOrderPlaced);
        OrderPlaced message = CreateOrderPlaced();

        await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(message);

        BasicGetResult received = (await channel.BasicGetAsync(QueueNames.AdminOrderPlaced, autoAck: true)).ShouldNotBeNull();
        received.BasicProperties.Persistent.ShouldBeTrue();
        received.BasicProperties.CorrelationId.ShouldBe(message.OrderId.ToString());
        received.BasicProperties.ContentType.ShouldBe("application/json");
        received.BasicProperties.Timestamp.UnixTime.ShouldBe(new DateTimeOffset(message.CreatedAt).ToUnixTimeSeconds());

        OrderPlaced body = JsonSerializer.Deserialize<OrderPlaced>(received.Body.Span, JsonOptions.Default).ShouldNotBeNull();
        body.OrderId.ShouldBe(message.OrderId);
        body.Items.Single().Extras.Single().Name.ShouldBe("Cheese");
        body.Total.ShouldBe(message.Total);
    }

    [Fact]
    public async Task PublishAsync_ShouldDeliverEachOfTheSixMessagesToItsOwnQueue()
    {
        using IHost host = await StartHostAsync();
        await using IChannel channel = await OpenChannelAsync(host);
        foreach (QueueBinding binding in RabbitMqTopology.Bindings)
        {
            await channel.QueuePurgeAsync(binding.Queue);
        }

        IMessagePublisher publisher = host.Services.GetRequiredService<IMessagePublisher>();
        MessageBase[] messages = CreateOneOfEachMessage();
        foreach (MessageBase message in messages)
        {
            await publisher.PublishAsync(message);
        }

        foreach (MessageBase message in messages)
        {
            string queue = RabbitMqTopology.Bindings.Single(b => b.MessageType == message.GetType()).Queue;
            BasicGetResult received = (await channel.BasicGetAsync(queue, autoAck: true)).ShouldNotBeNull();
            received.BasicProperties.Type.ShouldBe(message.GetType().Name);
            received.BasicProperties.MessageId.ShouldBe(message.MessageId.ToString());
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task PublishAsync_ShouldThrow_WhenNoQueueIsBoundToTheRoutingKey()
    {
        using IHost host = await StartHostAsync();
        await using IChannel channel = await OpenChannelAsync(host);
        await channel.QueueUnbindAsync(QueueNames.AdminDishRated, RabbitMqFixture.ExchangeName, RoutingKeys.DishRated);

        try
        {
            IMessagePublisher publisher = host.Services.GetRequiredService<IMessagePublisher>();

            await Should.ThrowAsync<PublishException>(() => publisher.PublishAsync(CreateDishRated()));
        }
        finally
        {
            await channel.QueueBindAsync(QueueNames.AdminDishRated, RabbitMqFixture.ExchangeName, RoutingKeys.DishRated);
            await host.StopAsync();
        }
    }

    private async Task<IHost> StartHostAsync()
    {
        IHost host = rabbitMq.CreateHost();
        await host.StartAsync();
        return host;
    }

    private static Task<IChannel> OpenChannelAsync(IHost host) =>
        host.Services.GetRequiredService<RabbitMqConnection>().CreateChannelAsync();

    private static OrderPlaced CreateOrderPlaced()
    {
        Guid orderId = Guid.NewGuid();
        return new OrderPlaced
        {
            CorrelationId = orderId,
            CreatedAt = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc),
            OrderId = orderId,
            OrderNumber = "FO-1",
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Customer",
            CustomerPhone = "+961 70 000 000",
            DeliveryType = DeliveryType.Pickup,
            PaymentMethod = PaymentMethod.Cash,
            Items =
            [
                new OrderItemDto
                {
                    MenuItemId = Guid.NewGuid(),
                    Name = "Fries",
                    UnitPrice = 3.00m,
                    Quantity = 1,
                    Extras = [new ExtraDto { Name = "Cheese", Price = 1.00m }]
                }
            ],
            Total = 4.00m,
            PlacedAt = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc)
        };
    }

    private static DishRated CreateDishRated() => new()
    {
        RatingId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        MenuItemId = Guid.NewGuid(),
        CustomerName = "Test Customer",
        Stars = 5,
        RatedAt = DateTime.UtcNow
    };

    private static MessageBase[] CreateOneOfEachMessage()
    {
        Guid orderId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        return
        [
            CreateOrderPlaced(),
            new PaymentCompleted { CorrelationId = orderId, OrderId = orderId, PaymentId = Guid.NewGuid(), Amount = 4.00m, Method = PaymentMethod.Online, PaidAt = now },
            new OrderCancelled { CorrelationId = orderId, OrderId = orderId, CancelledAt = now },
            CreateDishRated(),
            new OrderStatusChanged { CorrelationId = orderId, OrderId = orderId, NewStatus = OrderStatus.Accepted, EstimatedDeliveryTime = now.AddMinutes(30), ChangedAt = now },
            new MenuItemUpdated
            {
                Action = MenuItemAction.Created,
                MenuItemId = Guid.NewGuid(),
                Name = "Fries",
                Price = 3.00m,
                IsAvailable = true,
                Category = new CategoryDto { Id = Guid.NewGuid(), Name = "Sides", SortOrder = 1 },
                UpdatedAt = now
            }
        ];
    }
}
