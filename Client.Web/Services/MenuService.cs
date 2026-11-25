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
            Categories = categories.Select(ToCategory).ToList(),
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

    public async Task<List<DishCardViewModel>> GetFeaturedAsync(
        int count = HomeViewModel.FeaturedDishesCount,
        CancellationToken ct = default)
    {
        if (count <= 0)
        {
            return [];
        }

        List<MenuItem> dishes = await menu.GetFeaturedAsync(count, ct);
        return dishes.Select(ToDishCard).ToList();
    }

    public async Task<HomeViewModel> GetHomeAsync(CancellationToken ct = default)
    {
        List<Category> categories = await menu.GetCategoriesAsync(ct);
        List<DishCardViewModel> featured = await GetFeaturedAsync(HomeViewModel.FeaturedDishesCount, ct);

        return new HomeViewModel
        {
            Categories = categories.Select(ToCategory).ToList(),
            FeaturedDishes = featured,
            Recommendations = []
        };
    }

    private static MenuCategoryViewModel ToCategory(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name
    };

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
