using Shared.Contracts.Messages;

namespace Admin.Web.Services;

public interface IRatingService
{
    Task SaveRatingAsync(DishRated message, CancellationToken ct = default);
}
