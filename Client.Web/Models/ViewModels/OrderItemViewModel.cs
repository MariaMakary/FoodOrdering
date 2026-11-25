namespace Client.Web.Models.ViewModels;

public class OrderItemViewModel
{
    public Guid MenuItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<OrderItemExtraViewModel> Extras { get; set; } = [];

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public bool IsRated { get; set; }
}
