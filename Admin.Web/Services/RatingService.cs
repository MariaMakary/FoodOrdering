using Admin.Web.Models.Entities;
using Admin.Web.Repositories;
using Shared.Contracts.Messages;

namespace Admin.Web.Services;

public class RatingService(IRatingRepository ratings, ILogger<RatingService> logger) : IRatingService
{
    public async Task SaveRatingAsync(DishRated message, CancellationToken ct = default)
    {
        if (!await ratings.OrderExistsAsync(message.OrderId, ct))
        {
            throw new OrderNotReceivedException(message.OrderId, nameof(DishRated));
        }

        if (await ratings.ExistsAsync(message.RatingId, ct))
        {
            logger.LogInformation("Rating {RatingId} is already saved, ignoring the DishRated message", message.RatingId);
            return;
        }

        if (await ratings.ExistsForDishAsync(message.OrderId, message.MenuItemId, ct))
        {
            logger.LogWarning(
                "Menu item {MenuItemId} of order {OrderId} is already rated, ignoring the DishRated message for rating {RatingId}",
                message.MenuItemId, message.OrderId, message.RatingId);
            return;
        }

        ratings.Add(new Rating
        {
            Id = message.RatingId,
            OrderId = message.OrderId,
            MenuItemId = message.MenuItemId,
            CustomerName = message.CustomerName,
            Stars = message.Stars,
            Comment = message.Comment,
            RatedAt = message.RatedAt
        });

        await ratings.SaveChangesAsync(ct);

        logger.LogInformation("Rating {RatingId} of {Stars} stars saved for menu item {MenuItemId} of order {OrderId}",
            message.RatingId, message.Stars, message.MenuItemId, message.OrderId);
    }
}
