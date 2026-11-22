using Shared.Contracts.Enums;

namespace Client.Web.Models.Entities;

public class OrderStatusHistory
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Note { get; set; }

    public Order Order { get; set; } = null!;
}
