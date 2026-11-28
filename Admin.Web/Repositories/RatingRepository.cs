using Admin.Web.Data;
using Admin.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Admin.Web.Repositories;

public class RatingRepository(AdminDbContext context) : IRatingRepository
{
    public Task<bool> OrderExistsAsync(Guid orderId, CancellationToken ct = default) =>
        context.Orders.AnyAsync(o => o.Id == orderId, ct);

    public Task<bool> ExistsAsync(Guid ratingId, CancellationToken ct = default) =>
        context.Ratings.AnyAsync(r => r.Id == ratingId, ct);

    public Task<bool> ExistsForDishAsync(Guid orderId, Guid menuItemId, CancellationToken ct = default) =>
        context.Ratings.AnyAsync(r => r.OrderId == orderId && r.MenuItemId == menuItemId, ct);

    public void Add(Rating rating) => context.Ratings.Add(rating);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
