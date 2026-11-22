using Client.Web.Models.Enums;
using Shared.Contracts.Enums;
using Shared.Persistence;

namespace Client.Web.Models.Entities;

public class Payment : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = "Stripe";
    public string ProviderSessionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentAttemptStatus Status { get; set; }
    public DateTime? PaidAt { get; set; }

    public Order Order { get; set; } = null!;
}
