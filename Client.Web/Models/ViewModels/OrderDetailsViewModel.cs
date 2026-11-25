using Shared.Contracts.Enums;

namespace Client.Web.Models.ViewModels;

public class OrderDetailsViewModel
{
    public Guid Id { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public OrderStatus Status { get; set; }

    public DeliveryType DeliveryType { get; set; }

    public string? Address { get; set; }

    public string? Notes { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    public decimal Total { get; set; }

    public DateTime? EstimatedDeliveryTime { get; set; }

    public string? RejectionReason { get; set; }

    public IReadOnlyList<OrderItemViewModel> Items { get; set; } = [];

    public IReadOnlyList<OrderStatusHistoryViewModel> StatusHistory { get; set; } = [];

    public bool CanCancel { get; set; }

    public bool CanRate { get; set; }
}
