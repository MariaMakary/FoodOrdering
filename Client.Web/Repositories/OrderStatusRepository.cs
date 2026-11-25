using Client.Web.Data;
using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Client.Web.Repositories;

public class OrderStatusRepository(ClientDbContext context) : IOrderStatusRepository
{
    public Task<Order?> GetOrderAsync(Guid orderId, CancellationToken ct = default) =>
        context.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public void AddHistory(OrderStatusHistory history) => context.OrderStatusHistory.Add(history);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
