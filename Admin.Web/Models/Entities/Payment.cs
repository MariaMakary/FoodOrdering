using Shared.Contracts.Enums;
using Shared.Persistence;

namespace Admin.Web.Models.Entities;

public class Payment : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTime PaidAt { get; set; }

    public Order Order { get; set; } = null!;
}
