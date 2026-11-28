using Admin.Web.Models.Entities;

namespace Admin.Web.Repositories;

public interface IPaymentRepository
{
    Task<Order?> GetOrderWithPaymentAsync(Guid orderId, CancellationToken ct = default);

    void Add(Payment payment);

    Task SaveChangesAsync(CancellationToken ct = default);
}
