namespace Client.Web.Models.ViewModels;

public class CartItemViewModel
{
    public Guid CartItemId { get; set; }

    public Guid MenuItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<CartItemExtraViewModel> Extras { get; set; } = [];

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public bool IsAvailable { get; set; }
}
