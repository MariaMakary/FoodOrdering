using FluentAssertions;
using Moq;
using RabbitMQ.Client;
using Shared.Contracts.Messages;
using Shared.Contracts.Messaging;

namespace FoodOrdering.Tests.Contracts;

public class RabbitMqTopologyTests
{
    private const string Exchange = "food.events";

    [Fact]
    public void Bindings_AllMessageTypes_HaveExactlyOneBinding()
    {
        var messageTypes = typeof(MessageBase).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(MessageBase)) && !t.IsAbstract);

        RabbitMqTopology.Bindings.Select(b => b.MessageType).Should().BeEquivalentTo(messageTypes);
    }

    [Fact]
    public void Bindings_QueuesAndRoutingKeys_AreUnique()
    {
        RabbitMqTopology.Bindings.Select(b => b.Queue).Should().OnlyHaveUniqueItems();
        RabbitMqTopology.Bindings.Select(b => b.RoutingKey).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(typeof(OrderPlaced), "admin.")]
    [InlineData(typeof(PaymentCompleted), "admin.")]
    [InlineData(typeof(OrderCancelled), "admin.")]
    [InlineData(typeof(DishRated), "admin.")]
    [InlineData(typeof(OrderStatusChanged), "client.")]
    [InlineData(typeof(MenuItemUpdated), "client.")]
    public void Bindings_MessageType_GoesToQueueOfReceivingApp(Type messageType, string queuePrefix)
    {
        RabbitMqTopology.Bindings.Single(b => b.MessageType == messageType).Queue.Should().StartWith(queuePrefix);
    }

    [Fact]
    public void RoutingKeyFor_KnownMessage_ReturnsItsRoutingKey()
    {
        RabbitMqTopology.RoutingKeyFor(typeof(OrderStatusChanged)).Should().Be(RoutingKeys.OrderStatusChanged);
    }

    [Fact]
    public void RoutingKeyFor_TypeWithoutBinding_Throws()
    {
        var act = () => RabbitMqTopology.RoutingKeyFor(typeof(string));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task DeclareAsync_Channel_DeclaresDurableTopicExchange()
    {
        var channel = new Mock<IChannel>();

        await RabbitMqTopology.DeclareAsync(channel.Object, Exchange);

        channel.Verify(c => c.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, true, false,
            It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeclareAsync_Channel_DeclaresSixDurableQueuesBoundToExchange()
    {
        var channel = new Mock<IChannel>();

        await RabbitMqTopology.DeclareAsync(channel.Object, Exchange);

        foreach (var binding in RabbitMqTopology.Bindings)
        {
            channel.Verify(c => c.QueueDeclareAsync(binding.Queue, true, false, false,
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
            channel.Verify(c => c.QueueBindAsync(binding.Queue, Exchange, binding.RoutingKey,
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        channel.Verify(c => c.QueueDeclareAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
    }
}
