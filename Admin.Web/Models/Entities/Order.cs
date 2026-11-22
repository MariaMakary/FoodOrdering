using Shared.Contracts.Enums;
using Shared.Persistence;

namespace Admin.Web.Models.Entities;

public class Order : AuditableEntity
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public DeliveryType DeliveryType { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTime? EstimatedDeliveryTime { get; set; }
    public DateTime PlacedAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
    public ICollection<Rating> Ratings { get; set; } = new List<Rating>();
    public Payment? Payment { get; set; }
}
