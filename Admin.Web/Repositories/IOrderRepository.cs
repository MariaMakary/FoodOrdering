using Admin.Web.Models.Entities;
using Shared.Contracts.Enums;

namespace Admin.Web.Repositories;

public interface IOrderRepository
{
    Task<bool> ExistsAsync(Guid orderId, CancellationToken ct = default);

    Task<Order?> GetOrderAsync(Guid orderId, CancellationToken ct = default);

    Task<OrderStatusHistory?> GetLatestStatusChangeAsync(Guid orderId, OrderStatus status, CancellationToken ct = default);

    void Add(Order order);

    void AddHistory(OrderStatusHistory history);

    Task SaveChangesAsync(CancellationToken ct = default);
}
