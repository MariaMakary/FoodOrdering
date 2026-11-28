using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Client.Web.Repositories;

public class RatingRepository(ClientDbContext context) : IRatingRepository
{
    public Task<Order?> GetOrderForRatingAsync(Guid orderId, CancellationToken ct = default) =>
        context.Orders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(o => o.Items).ThenInclude(i => i.MenuItem)
            .Include(o => o.Ratings)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public Task<ApplicationUser?> GetCustomerAsync(Guid customerId, CancellationToken ct = default) =>
        context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == customerId, ct);

    public void Add(Rating rating) => context.Ratings.Add(rating);

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateRating(exception))
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }

    private bool IsDuplicateRating(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            return false;
        }

        IEntityType rating = context.Model.FindEntityType(typeof(Rating))!;
        IIndex orderDish = rating.GetIndexes().Single(i =>
            i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual([nameof(Rating.OrderId), nameof(Rating.MenuItemId)]));

        return postgres.TableName == rating.GetTableName() && postgres.ConstraintName == orderDish.GetDatabaseName();
    }
}
