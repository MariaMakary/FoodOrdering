using Client.Web.Models.Entities;

namespace Client.Web.Repositories;

public interface IMenuSyncRepository
{
    Task<Category?> GetCategoryAsync(Guid categoryId, CancellationToken ct = default);

    Task<MenuItem?> GetMenuItemWithExtrasAsync(Guid menuItemId, CancellationToken ct = default);

    void AddCategory(Category category);

    void AddMenuItem(MenuItem menuItem);

    void AddExtra(MenuItemExtra extra);

    void RemoveExtra(MenuItemExtra extra);

    Task SaveChangesAsync(CancellationToken ct = default);
}
