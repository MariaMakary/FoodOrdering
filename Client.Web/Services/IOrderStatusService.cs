using Shared.Contracts.Messages;

namespace Client.Web.Services;

public interface IOrderStatusService
{
    Task ApplyStatusChangeAsync(OrderStatusChanged message, CancellationToken ct = default);
}
