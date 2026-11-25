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

    public void Add(Order order) => context.Orders.Add(order);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
