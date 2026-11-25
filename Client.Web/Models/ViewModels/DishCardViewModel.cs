using System.ComponentModel.DataAnnotations;

namespace Client.Web.Models.ViewModels;

public class DishCardViewModel
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [DataType(DataType.Currency)]
    public decimal Price { get; set; }

    [Display(Name = "Photo")]
    public string? PhotoUrl { get; set; }

    [Display(Name = "Vegetarian")]
    public bool IsVegetarian { get; set; }

    [Display(Name = "Available")]
    public bool IsAvailable { get; set; }

    [Display(Name = "Category")]
    public string CategoryName { get; set; } = string.Empty;
}
