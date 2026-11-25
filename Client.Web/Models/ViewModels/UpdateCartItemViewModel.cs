using System.ComponentModel.DataAnnotations;

namespace Client.Web.Models.ViewModels;

public class UpdateCartItemViewModel
{
    [Required]
    public Guid CartItemId { get; set; }

    [Range(CartLimits.MinQuantity, CartLimits.MaxQuantity)]
    [Display(Name = "Quantity")]
    public int Quantity { get; set; }
}
