using Client.Web.Models.Entities;
using Client.Web.Repositories;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;

namespace Client.Web.Services;

public class MenuSyncService(IMenuSyncRepository menu, ILogger<MenuSyncService> logger) : IMenuSyncService
{
    public async Task ApplyMenuItemUpdatedAsync(MenuItemUpdated message, CancellationToken ct = default)
    {
        DateTime sourceUpdatedAt = ToDatabasePrecision(message.UpdatedAt);
        MenuItem? dish = await menu.GetMenuItemWithExtrasAsync(message.MenuItemId, ct);

        if (dish is not null && sourceUpdatedAt <= dish.SourceUpdatedAt)
        {
            logger.LogInformation(
                "Ignoring MenuItemUpdated ({Action}) for menu item {MenuItemId}: updated at {UpdatedAt}, last applied update at {SourceUpdatedAt}",
                message.Action, message.MenuItemId, message.UpdatedAt, dish.SourceUpdatedAt);
            return;
        }

        if (message.Action == MenuItemAction.Deleted)
        {
            await MarkDeletedAsync(dish, message, sourceUpdatedAt, ct);
            return;
        }

        await UpsertAsync(dish, message, sourceUpdatedAt, ct);
    }

    private static DateTime ToDatabasePrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);

    private async Task MarkDeletedAsync(MenuItem? dish, MenuItemUpdated message, DateTime sourceUpdatedAt, CancellationToken ct)
    {
        if (dish is null)
        {
            logger.LogWarning("Menu item {MenuItemId} does not exist, ignoring the MenuItemUpdated (Deleted)", message.MenuItemId);
            return;
        }

        dish.IsAvailable = false;
        dish.IsDeleted = true;
        dish.SourceUpdatedAt = sourceUpdatedAt;

        await menu.SaveChangesAsync(ct);

        logger.LogInformation("Menu item {MenuItemId} marked as deleted", dish.Id);
    }

    private async Task UpsertAsync(MenuItem? dish, MenuItemUpdated message, DateTime sourceUpdatedAt, CancellationToken ct)
    {
        if (message.Name is null || message.Price is null || message.Category is null)
        {
            throw new InvalidOperationException(
                $"MenuItemUpdated ({message.Action}) for menu item {message.MenuItemId} must carry a name, a price and a category.");
        }

        await UpsertCategoryAsync(message.Category, ct);

        bool isNew = dish is null;
        if (dish is null)
        {
            dish = new MenuItem { Id = message.MenuItemId };
            menu.AddMenuItem(dish);
        }

        dish.CategoryId = message.Category.Id;
        dish.Name = message.Name;
        dish.Description = message.Description;
        dish.Price = message.Price.Value;
        dish.PhotoUrl = message.PhotoUrl;
        dish.IsVegetarian = message.IsVegetarian;
        dish.IsAvailable = message.IsAvailable;
        dish.IsDeleted = false;
        dish.SourceUpdatedAt = sourceUpdatedAt;

        ReplaceExtras(dish, message.Extras);

        await menu.SaveChangesAsync(ct);

        logger.LogInformation("Menu item {MenuItemId} {Change} from MenuItemUpdated ({Action})",
            dish.Id, isNew ? "inserted" : "updated", message.Action);
    }

    private async Task UpsertCategoryAsync(CategoryDto source, CancellationToken ct)
    {
        Category? category = await menu.GetCategoryAsync(source.Id, ct);
        if (category is null)
        {
            menu.AddCategory(new Category { Id = source.Id, Name = source.Name, SortOrder = source.SortOrder });
            return;
        }

        category.Name = source.Name;
        category.SortOrder = source.SortOrder;
    }

    private void ReplaceExtras(MenuItem dish, IReadOnlyList<MenuItemExtraDto> offered)
    {
        Dictionary<Guid, MenuItemExtraDto> offeredById = offered
            .GroupBy(e => e.Id)
            .ToDictionary(g => g.Key, g => g.Last());

        foreach (MenuItemExtra removed in dish.Extras.Where(e => !offeredById.ContainsKey(e.Id)).ToList())
        {
            dish.Extras.Remove(removed);
            menu.RemoveExtra(removed);
        }

        foreach (MenuItemExtraDto source in offeredById.Values)
        {
            MenuItemExtra? existing = dish.Extras.FirstOrDefault(e => e.Id == source.Id);
            if (existing is not null)
            {
                existing.Name = source.Name;
                existing.Price = source.Price;
                continue;
            }

            var added = new MenuItemExtra { Id = source.Id, MenuItemId = dish.Id, Name = source.Name, Price = source.Price };
            dish.Extras.Add(added);
            menu.AddExtra(added);
        }
    }
}
