using Admin.Web.Models.Entities;
using Admin.Web.Repositories;
using Admin.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Contracts.Messages;
using Shouldly;

namespace FoodOrdering.UnitTests.Admin.Services;

public class RatingServiceTests
{
    private static readonly DateTime RatedAt = new(2026, 11, 24, 14, 10, 0, DateTimeKind.Utc);

    private readonly IRatingRepository _ratings = Substitute.For<IRatingRepository>();
    private readonly RatingService _service;

    public RatingServiceTests()
    {
        _service = new RatingService(_ratings, NullLogger<RatingService>.Instance);
    }

    [Fact]
    public async Task SaveRatingAsync_ShouldAddTheRatingWithTheMessageRatingIdAndSave()
    {
        DishRated message = GivenMessageForAnExistingOrder();

        await _service.SaveRatingAsync(message);

        Rating rating = AddedRating();
        rating.Id.ShouldBe(message.RatingId);
        rating.OrderId.ShouldBe(message.OrderId);
        rating.MenuItemId.ShouldBe(message.MenuItemId);
        rating.CustomerName.ShouldBe("Sara Haddad");
        rating.Stars.ShouldBe(5);
        rating.Comment.ShouldBe("Best burger in town!");
        rating.RatedAt.ShouldBe(RatedAt);
        rating.CreatedAt.ShouldBe(default);
        await _ratings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveRatingAsync_ShouldThrowSoTheMessageIsRetried_WhenTheOrderDoesNotExistYet()
    {
        DishRated message = Message();

        OrderNotReceivedException exception = await Should.ThrowAsync<OrderNotReceivedException>(
            () => _service.SaveRatingAsync(message));

        exception.OrderId.ShouldBe(message.OrderId);
        exception.MessageType.ShouldBe(nameof(DishRated));
        _ratings.DidNotReceive().Add(Arg.Any<Rating>());
        await _ratings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveRatingAsync_ShouldDoNothing_WhenTheRatingIdIsAlreadySaved()
    {
        DishRated message = GivenMessageForAnExistingOrder();
        _ratings.ExistsAsync(message.RatingId, Arg.Any<CancellationToken>()).Returns(true);

        await _service.SaveRatingAsync(message);

        _ratings.DidNotReceive().Add(Arg.Any<Rating>());
        await _ratings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveRatingAsync_ShouldDoNothing_WhenTheDishOfThisOrderIsAlreadyRated()
    {
        DishRated message = GivenMessageForAnExistingOrder();
        _ratings.ExistsForDishAsync(message.OrderId, message.MenuItemId, Arg.Any<CancellationToken>()).Returns(true);

        await _service.SaveRatingAsync(message);

        _ratings.DidNotReceive().Add(Arg.Any<Rating>());
        await _ratings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private DishRated GivenMessageForAnExistingOrder()
    {
        DishRated message = Message();
        _ratings.OrderExistsAsync(message.OrderId, Arg.Any<CancellationToken>()).Returns(true);
        return message;
    }

    private Rating AddedRating() =>
        _ratings.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IRatingRepository.Add))
            .GetArguments()[0]
            .ShouldBeOfType<Rating>();

    private static DishRated Message()
    {
        Guid orderId = Guid.NewGuid();
        return new DishRated
        {
            CorrelationId = orderId,
            RatingId = Guid.NewGuid(),
            OrderId = orderId,
            MenuItemId = Guid.NewGuid(),
            CustomerName = "Sara Haddad",
            Stars = 5,
            Comment = "Best burger in town!",
            RatedAt = RatedAt
        };
    }
}
