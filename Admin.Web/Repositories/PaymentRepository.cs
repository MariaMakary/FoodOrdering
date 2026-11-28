using Admin.Web.Data;
using Admin.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Admin.Web.Repositories;

public class PaymentRepository(AdminDbContext context) : IPaymentRepository
{
    public Task<Order?> GetOrderWithPaymentAsync(Guid orderId, CancellationToken ct = default) =>
        context.Orders
            .Include(o => o.Payment)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public void Add(Payment payment) => context.Payments.Add(payment);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
