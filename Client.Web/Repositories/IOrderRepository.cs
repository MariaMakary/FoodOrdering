using Client.Web.Models.Entities;
using Client.Web.Models.Identity;

namespace Client.Web.Repositories;

public interface IOrderRepository
{
    Task<ApplicationUser?> GetCustomerAsync(Guid customerId, CancellationToken ct = default);

    Task<long> NextOrderNumberAsync(CancellationToken ct = default);

    void Add(Order order);

    Task SaveChangesAsync(CancellationToken ct = default);
}
