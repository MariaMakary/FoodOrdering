using Shared.Contracts.Messages;

namespace Admin.Web.Services;

public interface IPaymentService
{
    Task RecordPaymentAsync(PaymentCompleted message, CancellationToken ct = default);
}
