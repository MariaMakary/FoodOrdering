using Shared.Contracts.Enums;

namespace Client.Web.Models.ViewModels;

public class OrderListItemViewModel
{
    public Guid Id { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public decimal Total { get; set; }

    public OrderStatus Status { get; set; }

    public PaymentStatus PaymentStatus { get; set; }
}
