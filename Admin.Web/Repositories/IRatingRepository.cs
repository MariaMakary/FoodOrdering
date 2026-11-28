using Admin.Web.Models.Entities;

namespace Admin.Web.Repositories;

public interface IRatingRepository
{
    Task<bool> OrderExistsAsync(Guid orderId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid ratingId, CancellationToken ct = default);

    Task<bool> ExistsForDishAsync(Guid orderId, Guid menuItemId, CancellationToken ct = default);

    void Add(Rating rating);

    Task SaveChangesAsync(CancellationToken ct = default);
}
