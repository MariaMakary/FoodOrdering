namespace Client.Web.Models.ViewModels;

public class CartViewModel
{
    public IReadOnlyList<CartItemViewModel> Items { get; set; } = [];

    public decimal Subtotal { get; set; }

    public bool IsEmpty => Items.Count == 0;

    public bool HasUnavailableItems => Items.Any(i => !i.IsAvailable);
}
