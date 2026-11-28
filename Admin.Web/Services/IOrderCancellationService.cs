using Shared.Contracts.Messages;

namespace Admin.Web.Services;

public interface IOrderCancellationService
{
    Task ApplyCancelAsync(OrderCancelled message, CancellationToken ct = default);
}
