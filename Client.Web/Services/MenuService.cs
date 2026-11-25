using Client.Web.Models.Entities;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;

namespace Client.Web.Services;

public class MenuService(IMenuRepository menu) : IMenuService
{
    public async Task<MenuIndexViewModel> GetMenuAsync(
        Guid? categoryId,
        string? search,
        bool vegetarian,
        decimal? maxPrice,
        CancellationToken ct = default)
    {
        string? trimmedSearch = CleanSearch(search);
        decimal? validMaxPrice = maxPrice < 0 ? null : maxPrice;

        List<Category> categories = await menu.GetCategoriesAsync(ct);
        List<MenuItem> dishes = await menu.GetDishesAsync(categoryId, trimmedSearch, vegetarian, validMaxPrice, ct);

        return new MenuIndexViewModel
        {
            Categories = categories.Select(c => new MenuCategoryViewModel { Id = c.Id, Name = c.Name }).ToList(),
            Dishes = dishes.Select(ToDishCard).ToList(),
            SelectedCategoryId = categoryId,
            Search = trimmedSearch,
            Vegetarian = vegetarian,
            MaxPrice = validMaxPrice
        };
    }

    private static string? CleanSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        string trimmed = search.Trim();
        return trimmed.Length <= MenuIndexViewModel.SearchMaxLength
            ? trimmed
            : trimmed[..MenuIndexViewModel.SearchMaxLength].TrimEnd();
    }

    public async Task<MenuItemDetailsViewModel?> GetDishDetailsAsync(Guid id, CancellationToken ct = default)
    {
        MenuItem? dish = await menu.GetByIdAsync(id, ct);
        if (dish is null)
        {
            return null;
        }

        return new MenuItemDetailsViewModel
        {
            Id = dish.Id,
            Name = dish.Name,
            Description = dish.Description,
            Price = dish.Price,
            PhotoUrl = dish.PhotoUrl,
            IsVegetarian = dish.IsVegetarian,
            IsAvailable = dish.IsAvailable,
            CategoryId = dish.CategoryId,
            CategoryName = dish.Category.Name,
            Extras = dish.Extras
                .Select(e => new MenuItemExtraViewModel { Id = e.Id, Name = e.Name, Price = e.Price })
                .ToList()
        };
    }

    private static DishCardViewModel ToDishCard(MenuItem dish) => new()
    {
        Id = dish.Id,
        Name = dish.Name,
        Price = dish.Price,
        PhotoUrl = dish.PhotoUrl,
        IsVegetarian = dish.IsVegetarian,
        IsAvailable = dish.IsAvailable,
        CategoryName = dish.Category.Name
    };
}
