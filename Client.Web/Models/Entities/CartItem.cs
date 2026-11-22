using Shared.Persistence;

namespace Client.Web.Models.Entities;

public class CartItem : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid CartId { get; set; }
    public Guid MenuItemId { get; set; }
    public int Quantity { get; set; }

    public Cart Cart { get; set; } = null!;
    public MenuItem MenuItem { get; set; } = null!;
    public ICollection<CartItemExtra> Extras { get; set; } = new List<CartItemExtra>();
}
