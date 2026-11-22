using Shared.Persistence;

namespace Admin.Web.Models.Entities;

public class OrderItemExtra : IAuditable
{
    public Guid Id { get; set; }
    public Guid OrderItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public OrderItem OrderItem { get; set; } = null!;
}
