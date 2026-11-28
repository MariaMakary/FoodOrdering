using Shared.Contracts.Enums;

namespace Client.Web.Models.ViewModels;

public class OrderConfirmationViewModel
{
    public Guid OrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public IReadOnlyList<OrderItemViewModel> Items { get; set; } = [];

    public decimal Total { get; set; }

    public DeliveryType DeliveryType { get; set; }

    public string? Address { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    public OrderStatus Status { get; set; }

    public DateTime? EstimatedTime { get; set; }
}
