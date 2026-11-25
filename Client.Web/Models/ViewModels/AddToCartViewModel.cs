using System.ComponentModel.DataAnnotations;

namespace Client.Web.Models.ViewModels;

public class AddToCartViewModel
{
    [Required]
    public Guid MenuItemId { get; set; }

    [Range(CartLimits.MinQuantity, CartLimits.MaxQuantity)]
    [Display(Name = "Quantity")]
    public int Quantity { get; set; } = 1;

    [Display(Name = "Extras")]
    public List<Guid> ExtraIds { get; set; } = [];
}
