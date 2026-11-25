using Client.Web.Data;
using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Client.Web.Repositories;

public class MenuSyncRepository(ClientDbContext context) : IMenuSyncRepository
{
    public Task<Category?> GetCategoryAsync(Guid categoryId, CancellationToken ct = default) =>
        context.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct);

    public Task<MenuItem?> GetMenuItemWithExtrasAsync(Guid menuItemId, CancellationToken ct = default) =>
        context.MenuItems
            .Include(m => m.Extras)
            .SingleOrDefaultAsync(m => m.Id == menuItemId, ct);

    public void AddCategory(Category category) => context.Categories.Add(category);

    public void AddMenuItem(MenuItem menuItem) => context.MenuItems.Add(menuItem);

    public void AddExtra(MenuItemExtra extra) => context.MenuItemExtras.Add(extra);

    public void RemoveExtra(MenuItemExtra extra) => context.MenuItemExtras.Remove(extra);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
