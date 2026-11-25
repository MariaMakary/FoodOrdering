using Client.Web.Models.Entities;

namespace Client.Web.Repositories;

public interface IMenuRepository
{
    Task<List<MenuItem>> GetDishesAsync(
        Guid? categoryId,
        string? search,
        bool vegetarian,
        decimal? maxPrice,
        CancellationToken ct = default);

    Task<List<Category>> GetCategoriesAsync(CancellationToken ct = default);

    Task<MenuItem?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<List<MenuItem>> GetFeaturedAsync(int count, CancellationToken ct = default);
}
