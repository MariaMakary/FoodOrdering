using NSubstitute;
using RabbitMQ.Client;
using Shared.Contracts.Messages;
using Shared.Contracts.Messaging;
using Shared.Messaging;
using Shouldly;

namespace FoodOrdering.UnitTests.Messaging;

public class RabbitMqTopologyTests
{
    private const string Exchange = "food.events";

    [Fact]
    public void Bindings_ShouldHaveExactlyOneBindingForEveryMessageType()
    {
        IEnumerable<Type> messageTypes = typeof(MessageBase).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(MessageBase)) && !t.IsAbstract);

        RabbitMqTopology.Bindings.Select(b => b.MessageType).ShouldBe(messageTypes, ignoreOrder: true);
    }

    [Fact]
    public void Bindings_ShouldUseEachQueueAndRoutingKeyOnlyOnce()
    {
        RabbitMqTopology.Bindings.Select(b => b.Queue).ShouldBeUnique();
        RabbitMqTopology.Bindings.Select(b => b.RoutingKey).ShouldBeUnique();
    }

    [Theory]
    [InlineData(typeof(OrderPlaced), "admin.")]
    [InlineData(typeof(PaymentCompleted), "admin.")]
    [InlineData(typeof(OrderCancelled), "admin.")]
    [InlineData(typeof(DishRated), "admin.")]
    [InlineData(typeof(OrderStatusChanged), "client.")]
    [InlineData(typeof(MenuItemUpdated), "client.")]
    public void Bindings_ShouldRouteEachMessageToTheQueueOfTheReceivingApp(Type messageType, string queuePrefix)
    {
        RabbitMqTopology.Bindings.Single(b => b.MessageType == messageType).Queue.ShouldStartWith(queuePrefix);
    }

    [Fact]
    public void RoutingKeyFor_ShouldReturnTheRoutingKey_WhenTheMessageIsKnown()
    {
        RabbitMqTopology.RoutingKeyFor(typeof(OrderStatusChanged)).ShouldBe(RoutingKeys.OrderStatusChanged);
    }

    [Fact]
    public void RoutingKeyFor_ShouldThrow_WhenTheTypeHasNoBinding()
    {
        Should.Throw<ArgumentException>(() => RabbitMqTopology.RoutingKeyFor(typeof(string)));
    }

    [Fact]
    public async Task DeclareAsync_ShouldDeclareADurableTopicExchange()
    {
        IChannel channel = Substitute.For<IChannel>();

        await RabbitMqTopology.DeclareAsync(channel, Exchange);

        await channel.Received(1).ExchangeDeclareAsync(
            Exchange, ExchangeType.Topic, Arg.Is(true), Arg.Is(false),
            Arg.Any<IDictionary<string, object?>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeclareAsync_ShouldDeclareSixDurableQueuesBoundToTheExchange()
    {
        IChannel channel = Substitute.For<IChannel>();

        await RabbitMqTopology.DeclareAsync(channel, Exchange);

        foreach (QueueBinding binding in RabbitMqTopology.Bindings)
        {
            await channel.Received(1).QueueDeclareAsync(
                binding.Queue, Arg.Is(true), Arg.Is(false), Arg.Is(false),
                Arg.Any<IDictionary<string, object?>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
            await channel.Received(1).QueueBindAsync(
                binding.Queue, Exchange, binding.RoutingKey,
                Arg.Any<IDictionary<string, object?>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        await channel.Received(6).QueueDeclareAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(),
            Arg.Any<IDictionary<string, object?>>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
