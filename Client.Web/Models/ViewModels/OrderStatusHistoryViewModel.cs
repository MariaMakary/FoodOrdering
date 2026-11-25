using Shared.Contracts.Enums;

namespace Client.Web.Models.ViewModels;

public class OrderStatusHistoryViewModel
{
    public OrderStatus Status { get; set; }

    public DateTime ChangedAt { get; set; }

    public string? Note { get; set; }
}
