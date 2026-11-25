using Client.Web.Models.Entities;

namespace Client.Web.Repositories;

public interface IOrderStatusRepository
{
    Task<Order?> GetOrderAsync(Guid orderId, CancellationToken ct = default);

    void AddHistory(OrderStatusHistory history);

    Task SaveChangesAsync(CancellationToken ct = default);
}
