using Admin.Web.Data;
using Admin.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Admin.Web.Repositories;

public class OrderRepository(AdminDbContext context) : IOrderRepository
{
    public Task<bool> ExistsAsync(Guid orderId, CancellationToken ct = default) =>
        context.Orders.AnyAsync(o => o.Id == orderId, ct);

    public void Add(Order order) => context.Orders.Add(order);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
