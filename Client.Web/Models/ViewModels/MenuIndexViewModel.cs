using System.ComponentModel.DataAnnotations;

namespace Client.Web.Models.ViewModels;

public class MenuIndexViewModel
{
    public const int SearchMaxLength = 100;

    public IReadOnlyList<MenuCategoryViewModel> Categories { get; set; } = [];

    public IReadOnlyList<DishCardViewModel> Dishes { get; set; } = [];

    [Display(Name = "Category")]
    public Guid? SelectedCategoryId { get; set; }

    [StringLength(SearchMaxLength)]
    [Display(Name = "Search")]
    public string? Search { get; set; }

    [Display(Name = "Vegetarian only")]
    public bool Vegetarian { get; set; }

    [Range(typeof(decimal), "0", "10000")]
    [DataType(DataType.Currency)]
    [Display(Name = "Max price")]
    public decimal? MaxPrice { get; set; }
}
