using Shared.Persistence;

namespace Client.Web.Models.Entities;

public class OrderItem : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid MenuItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    public Order Order { get; set; } = null!;
    public MenuItem MenuItem { get; set; } = null!;
    public ICollection<OrderItemExtra> Extras { get; set; } = new List<OrderItemExtra>();
}
