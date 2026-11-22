namespace Client.Web.Models.Entities;

public class CartItemExtra
{
    public Guid CartItemId { get; set; }
    public Guid MenuItemExtraId { get; set; }

    public CartItem CartItem { get; set; } = null!;
    public MenuItemExtra MenuItemExtra { get; set; } = null!;
}
