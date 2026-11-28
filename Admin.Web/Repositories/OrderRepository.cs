using Admin.Web.Data;
using Admin.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;

namespace Admin.Web.Repositories;

public class OrderRepository(AdminDbContext context) : IOrderRepository
{
    public Task<bool> ExistsAsync(Guid orderId, CancellationToken ct = default) =>
        context.Orders.AnyAsync(o => o.Id == orderId, ct);

    public Task<Order?> GetOrderAsync(Guid orderId, CancellationToken ct = default) =>
        context.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public Task<OrderStatusHistory?> GetLatestStatusChangeAsync(Guid orderId, OrderStatus status, CancellationToken ct = default) =>
        context.OrderStatusHistory
            .AsNoTracking()
            .Where(h => h.OrderId == orderId && h.Status == status)
            .OrderByDescending(h => h.ChangedAt)
            .FirstOrDefaultAsync(ct);

    public void Add(Order order) => context.Orders.Add(order);

    public void AddHistory(OrderStatusHistory history) => context.OrderStatusHistory.Add(history);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
