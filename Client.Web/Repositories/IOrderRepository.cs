using Client.Web.Models.Entities;
using Client.Web.Models.Identity;

namespace Client.Web.Repositories;

public interface IOrderRepository
{
    Task<ApplicationUser?> GetCustomerAsync(Guid customerId, CancellationToken ct = default);

    Task<long> NextOrderNumberAsync(CancellationToken ct = default);

    Task<List<Order>> GetCustomerOrdersAsync(Guid customerId, CancellationToken ct = default);

    Task<Order?> GetOrderWithDetailsAsync(Guid orderId, CancellationToken ct = default);

    Task<Order?> GetOrderForUpdateAsync(Guid orderId, CancellationToken ct = default);

    void Add(Order order);

    void AddHistory(OrderStatusHistory history);

    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken ct = default);
}
