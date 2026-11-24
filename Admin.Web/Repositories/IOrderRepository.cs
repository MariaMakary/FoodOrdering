using Admin.Web.Models.Entities;

namespace Admin.Web.Repositories;

public interface IOrderRepository
{
    Task<bool> ExistsAsync(Guid orderId, CancellationToken ct = default);

    void Add(Order order);

    Task SaveChangesAsync(CancellationToken ct = default);
}
