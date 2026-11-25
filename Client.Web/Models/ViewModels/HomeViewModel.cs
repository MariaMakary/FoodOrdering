namespace Client.Web.Models.ViewModels;

public class HomeViewModel
{
    public const int FeaturedDishesCount = 6;

    public IReadOnlyList<MenuCategoryViewModel> Categories { get; set; } = [];

    public IReadOnlyList<DishCardViewModel> FeaturedDishes { get; set; } = [];

    public IReadOnlyList<DishCardViewModel> Recommendations { get; set; } = [];
}
