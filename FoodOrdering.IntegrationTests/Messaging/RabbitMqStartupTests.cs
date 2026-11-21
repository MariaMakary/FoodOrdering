using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Shared.Messaging;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Messaging;

[Collection(RabbitMqCollection.Name)]
public class RabbitMqStartupTests(RabbitMqFixture rabbitMq)
{
    [Fact]
    public async Task StartAsync_ShouldDeclareTheExchangeAndEveryQueue()
    {
        using IHost host = rabbitMq.CreateHost();
        await host.StartAsync();

        RabbitMqConnection connection = host.Services.GetRequiredService<RabbitMqConnection>();
        await using IChannel channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclarePassiveAsync(RabbitMqFixture.ExchangeName);
        foreach (QueueBinding binding in RabbitMqTopology.Bindings)
        {
            QueueDeclareOk queue = await channel.QueueDeclarePassiveAsync(binding.Queue);
            queue.QueueName.ShouldBe(binding.Queue);
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnTheSameConnection_WhenCalledMoreThanOnce()
    {
        using IHost host = rabbitMq.CreateHost();
        await host.StartAsync();

        RabbitMqConnection connection = host.Services.GetRequiredService<RabbitMqConnection>();

        IConnection first = await connection.GetAsync();
        IConnection second = await connection.GetAsync();

        second.ShouldBeSameAs(first);
        first.IsOpen.ShouldBeTrue();

        await host.StopAsync();
    }
}
