using Client.Web.Data;
using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Client.Web.Repositories;

public class MenuRepository(ClientDbContext context) : IMenuRepository
{
    public Task<List<MenuItem>> GetDishesAsync(
        Guid? categoryId,
        string? search,
        bool vegetarian,
        decimal? maxPrice,
        CancellationToken ct = default)
    {
        IQueryable<MenuItem> dishes = context.MenuItems
            .AsNoTracking()
            .Include(m => m.Category)
            .Where(m => !m.IsDeleted);

        if (categoryId is not null)
        {
            dishes = dishes.Where(m => m.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string pattern = $"%{EscapeLikePattern(search.Trim())}%";
            dishes = dishes.Where(m =>
                EF.Functions.ILike(m.Name, pattern) ||
                (m.Description != null && EF.Functions.ILike(m.Description, pattern)));
        }

        if (vegetarian)
        {
            dishes = dishes.Where(m => m.IsVegetarian);
        }

        if (maxPrice is not null)
        {
            dishes = dishes.Where(m => m.Price <= maxPrice);
        }

        return dishes
            .OrderBy(m => m.Category.SortOrder)
            .ThenBy(m => m.Category.Name)
            .ThenBy(m => m.Name)
            .ToListAsync(ct);
    }

    public Task<List<Category>> GetCategoriesAsync(CancellationToken ct = default) =>
        context.Categories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

    public Task<MenuItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        context.MenuItems
            .AsNoTracking()
            .Include(m => m.Category)
            .Include(m => m.Extras.OrderBy(e => e.Name))
            .SingleOrDefaultAsync(m => m.Id == id && !m.IsDeleted, ct);

    public Task<List<MenuItem>> GetFeaturedAsync(int count, CancellationToken ct = default) =>
        context.MenuItems
            .AsNoTracking()
            .Include(m => m.Category)
            .Where(m => m.IsAvailable && !m.IsDeleted)
            .OrderBy(m => !context.Ratings.Any(r => r.MenuItemId == m.Id))
            .ThenByDescending(m => context.Ratings.Where(r => r.MenuItemId == m.Id).Average(r => (double?)r.Stars))
            .ThenByDescending(m => context.Ratings.Count(r => r.MenuItemId == m.Id))
            .ThenByDescending(m => m.CreatedAt)
            .ThenBy(m => m.Name)
            .Take(count)
            .ToListAsync(ct);

    private static string EscapeLikePattern(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
