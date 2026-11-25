using Client.Web.Models.ViewModels;

namespace Client.Web.Services;

public interface IMenuService
{
    Task<MenuIndexViewModel> GetMenuAsync(
        Guid? categoryId,
        string? search,
        bool vegetarian,
        decimal? maxPrice,
        CancellationToken ct = default);

    Task<MenuItemDetailsViewModel?> GetDishDetailsAsync(Guid id, CancellationToken ct = default);

    Task<List<DishCardViewModel>> GetFeaturedAsync(
        int count = HomeViewModel.FeaturedDishesCount,
        CancellationToken ct = default);

    Task<HomeViewModel> GetHomeAsync(CancellationToken ct = default);
}
