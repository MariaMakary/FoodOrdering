using Client.Web.Data;
using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Microsoft.EntityFrameworkCore;

namespace Client.Web.Repositories;

public class OrderRepository(ClientDbContext context) : IOrderRepository
{
    private const string NextOrderNumberSql =
        $"SELECT nextval('\"{ClientDbContext.OrderNumberSequence}\"') AS \"Value\"";

    public Task<ApplicationUser?> GetCustomerAsync(Guid customerId, CancellationToken ct = default) =>
        context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == customerId, ct);

    public Task<long> NextOrderNumberAsync(CancellationToken ct = default) =>
        context.Database.SqlQueryRaw<long>(NextOrderNumberSql).SingleAsync(ct);

    public Task<List<Order>> GetCustomerOrdersAsync(Guid customerId, CancellationToken ct = default) =>
        context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.OrderNumber)
            .ToListAsync(ct);

    public Task<Order?> GetOrderWithDetailsAsync(Guid orderId, CancellationToken ct = default) =>
        context.Orders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(o => o.Items).ThenInclude(i => i.Extras)
            .Include(o => o.StatusHistory)
            .Include(o => o.Ratings)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public Task<Order?> GetOrderForUpdateAsync(Guid orderId, CancellationToken ct = default) =>
        context.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public void Add(Order order) => context.Orders.Add(order);

    public void AddHistory(OrderStatusHistory history) => context.OrderStatusHistory.Add(history);

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
