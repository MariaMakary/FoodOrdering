using System.Text;
using System.Text.Json;
using FoodOrdering.UnitTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Contracts.Messages;
using Shared.Contracts.Serialization;
using Shared.Messaging;
using Shouldly;

namespace FoodOrdering.UnitTests.Messaging;

public sealed class RabbitMqConsumerBaseTests : IDisposable
{
    private const ulong DeliveryTag = 7;

    private readonly IProcessedMessageStore _store = Substitute.For<IProcessedMessageStore>();
    private readonly IDishRatedHandler _handler = Substitute.For<IDishRatedHandler>();
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly ListLoggerProvider _logs = new();
    private readonly ServiceProvider _services;
    private readonly TestConsumer _consumer;

    public RabbitMqConsumerBaseTests()
    {
        _services = new ServiceCollection()
            .AddScoped(_ => _store)
            .AddSingleton(_handler)
            .AddLogging(logging => logging.AddProvider(_logs))
            .BuildServiceProvider();

        var connection = new RabbitMqConnection(
            Options.Create(new RabbitMqOptions()),
            Substitute.For<IHostEnvironment>(),
            NullLogger<RabbitMqConnection>.Instance);

        _consumer = new TestConsumer(
            connection,
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<ILogger<TestConsumer>>());
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldMarkThenHandleThenAck_WhenTheMessageIsNew()
    {
        DishRated message = NewMessage();

        await _consumer.OnReceivedAsync(_channel, Delivery(message), CancellationToken.None);

        Received.InOrder(() =>
        {
            _ = _store.IsProcessedAsync(message.MessageId, Arg.Any<CancellationToken>());
            _store.MarkProcessed(message.MessageId, nameof(DishRated));
            _ = _handler.HandleAsync(Arg.Is<DishRated>(m => m.MessageId == message.MessageId), Arg.Any<CancellationToken>());
            _ = _channel.BasicAckAsync(DeliveryTag, false, Arg.Any<CancellationToken>());
        });
        await _channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldAckWithoutHandling_WhenTheMessageWasAlreadyProcessed()
    {
        DishRated message = NewMessage();
        _store.IsProcessedAsync(message.MessageId, Arg.Any<CancellationToken>()).Returns(true);

        await _consumer.OnReceivedAsync(_channel, Delivery(message), CancellationToken.None);

        await _handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);
        _store.DidNotReceiveWithAnyArgs().MarkProcessed(default, default!);
        await _channel.Received(1).BasicAckAsync(DeliveryTag, false, Arg.Any<CancellationToken>());
        await _channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.StartsWith("Duplicate skipped"));
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldAck_WhenTheSaveFailsBecauseAnotherCopyWasProcessedAtTheSameTime()
    {
        DishRated message = NewMessage();
        var duplicate = new InvalidOperationException("Simulated duplicate key.");
        _handler.HandleAsync(Arg.Any<DishRated>(), Arg.Any<CancellationToken>()).ThrowsAsync(duplicate);
        _store.IsDuplicate(duplicate).Returns(true);

        await _consumer.OnReceivedAsync(_channel, Delivery(message), CancellationToken.None);

        await _channel.Received(1).BasicAckAsync(DeliveryTag, false, Arg.Any<CancellationToken>());
        await _channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, default);
        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.StartsWith("Duplicate skipped"));
        _logs.Entries.ShouldNotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldNackWithoutRequeue_WhenTheHandlerFails()
    {
        DishRated message = NewMessage();
        var failure = new InvalidOperationException("Simulated handler failure.");
        _handler.HandleAsync(Arg.Any<DishRated>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _store.IsDuplicate(failure).Returns(false);

        await _consumer.OnReceivedAsync(_channel, Delivery(message), CancellationToken.None);

        await _channel.Received(1).BasicNackAsync(DeliveryTag, false, false, Arg.Any<CancellationToken>());
        await _channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, default);
        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Exception == failure);
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldCheckTheMessageIdFromTheBody_WhenThePropertyIsMissing()
    {
        DishRated message = NewMessage();

        await _consumer.OnReceivedAsync(_channel, Delivery(message, propertyMessageId: null), CancellationToken.None);

        await _store.Received(1).IsProcessedAsync(message.MessageId, Arg.Any<CancellationToken>());
        _store.Received(1).MarkProcessed(message.MessageId, nameof(DishRated));
    }

    [Fact]
    public async Task OnReceivedAsync_ShouldNackWithoutCheckingTheStore_WhenTheBodyIsNotValidJson()
    {
        BasicDeliverEventArgs delivery = Delivery(Encoding.UTF8.GetBytes("{ not json"), propertyMessageId: Guid.NewGuid().ToString());

        await _consumer.OnReceivedAsync(_channel, delivery, CancellationToken.None);

        await _store.DidNotReceiveWithAnyArgs().IsProcessedAsync(default, default);
        await _channel.Received(1).BasicNackAsync(DeliveryTag, false, false, Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        _services.Dispose();
        _logs.Dispose();
    }

    private static DishRated NewMessage() => new()
    {
        RatingId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        MenuItemId = Guid.NewGuid(),
        CustomerName = "Sara Haddad",
        Stars = 5,
        RatedAt = DateTime.UtcNow
    };

    private static BasicDeliverEventArgs Delivery(DishRated message) =>
        Delivery(message, message.MessageId.ToString());

    private static BasicDeliverEventArgs Delivery(DishRated message, string? propertyMessageId) =>
        Delivery(JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions.Default), propertyMessageId);

    private static BasicDeliverEventArgs Delivery(byte[] body, string? propertyMessageId) =>
        new(
            "test-consumer",
            DeliveryTag,
            redelivered: false,
            "food.events",
            RabbitMqTopology.RoutingKeyFor(typeof(DishRated)),
            new BasicProperties { MessageId = propertyMessageId, Type = nameof(DishRated) },
            body);

    public interface IDishRatedHandler
    {
        Task HandleAsync(DishRated message, CancellationToken ct);
    }

    public sealed class TestConsumer(RabbitMqConnection connection, IServiceScopeFactory scopeFactory, ILogger<TestConsumer> logger)
        : RabbitMqConsumerBase<DishRated>(connection, scopeFactory, logger)
    {
        public override Task HandleAsync(DishRated message, IServiceProvider services, CancellationToken ct) =>
            services.GetRequiredService<IDishRatedHandler>().HandleAsync(message, ct);
    }
}
