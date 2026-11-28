using System.Diagnostics.CodeAnalysis;
using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;

namespace Client.Web.Services;

public class RatingService(IRatingRepository ratings, IOutbox outbox) : IRatingService
{
    public async Task<ServiceResult<RateOrderViewModel>> GetRateFormAsync(
        Guid orderId,
        Guid customerId,
        CancellationToken ct = default)
    {
        Order? order = await ratings.GetOrderForRatingAsync(orderId, ct);
        if (!IsRateable(order, customerId))
        {
            return ServiceResult<RateOrderViewModel>.Failure(RatingErrors.OrderNotFound);
        }

        List<RateDishViewModel> dishes = UnratedDishes(order);
        if (dishes.Count == 0)
        {
            return ServiceResult<RateOrderViewModel>.Failure(RatingErrors.AlreadyRated);
        }

        return ServiceResult<RateOrderViewModel>.Success(new RateOrderViewModel
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Dishes = dishes
        });
    }

    public async Task<ServiceResult> SubmitAsync(Guid customerId, RateOrderViewModel model, CancellationToken ct = default)
    {
        Order? order = await ratings.GetOrderForRatingAsync(model.OrderId, ct);
        if (!IsRateable(order, customerId))
        {
            return ServiceResult.Failure(RatingErrors.OrderNotFound);
        }

        ServiceError? invalid = Validate(order, model.Dishes);
        if (invalid is not null)
        {
            return ServiceResult.Failure(invalid);
        }

        ApplicationUser? customer = await ratings.GetCustomerAsync(customerId, ct);
        if (customer is null)
        {
            return ServiceResult.Failure(RatingErrors.CustomerNotFound);
        }

        DateTime ratedAt = DateTime.UtcNow;
        string customerName = $"{customer.FirstName} {customer.LastName}".Trim();
        foreach (RateDishViewModel dish in model.Dishes.Where(d => d.Stars.HasValue))
        {
            var rating = new Rating
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                MenuItemId = dish.MenuItemId,
                CustomerId = customerId,
                Stars = dish.Stars!.Value,
                Comment = Clean(dish.Comment)
            };

            ratings.Add(rating);
            outbox.Add(ToMessage(rating, customerName, ratedAt));
        }

        return await ratings.TrySaveChangesAsync(ct)
            ? ServiceResult.Success()
            : ServiceResult.Failure(RatingErrors.AlreadyRated);
    }

    private static bool IsRateable([NotNullWhen(true)] Order? order, Guid customerId) =>
        order is not null && order.CustomerId == customerId && order.Status == OrderStatus.Delivered;

    private static ServiceError? Validate(Order order, IReadOnlyList<RateDishViewModel> dishes)
    {
        HashSet<Guid> orderedDishIds = order.Items.Select(i => i.MenuItemId).ToHashSet();
        HashSet<Guid> ratedDishIds = order.Ratings.Select(r => r.MenuItemId).ToHashSet();
        List<Guid> postedDishIds = dishes.Select(d => d.MenuItemId).ToList();

        if (postedDishIds.Count == 0
            || postedDishIds.Distinct().Count() != postedDishIds.Count
            || !postedDishIds.All(orderedDishIds.Contains))
        {
            return RatingErrors.InvalidDishes;
        }

        List<RateDishViewModel> rated = dishes.Where(d => d.Stars.HasValue).ToList();
        if (rated.Count == 0)
        {
            return RatingErrors.NothingRated;
        }

        if (rated.Any(d => ratedDishIds.Contains(d.MenuItemId)))
        {
            return RatingErrors.AlreadyRated;
        }

        if (rated.Any(d => d.Stars is < RateDishViewModel.MinStars or > RateDishViewModel.MaxStars))
        {
            return RatingErrors.InvalidStars;
        }

        return null;
    }

    private static List<RateDishViewModel> UnratedDishes(Order order)
    {
        HashSet<Guid> ratedDishIds = order.Ratings.Select(r => r.MenuItemId).ToHashSet();

        return order.Items
            .Where(i => !ratedDishIds.Contains(i.MenuItemId))
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Name)
            .GroupBy(i => i.MenuItemId)
            .Select(g => g.First())
            .Select(i => new RateDishViewModel
            {
                MenuItemId = i.MenuItemId,
                Name = i.Name,
                PhotoUrl = i.MenuItem.PhotoUrl
            })
            .ToList();
    }

    private static DishRated ToMessage(Rating rating, string customerName, DateTime ratedAt) => new()
    {
        CreatedAt = ratedAt,
        CorrelationId = rating.OrderId,
        RatingId = rating.Id,
        OrderId = rating.OrderId,
        MenuItemId = rating.MenuItemId,
        CustomerName = customerName,
        Stars = rating.Stars,
        Comment = rating.Comment,
        RatedAt = ratedAt
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
