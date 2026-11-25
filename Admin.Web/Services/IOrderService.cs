using Shared.Contracts.Messages;

namespace Admin.Web.Services;

public interface IOrderService
{
    Task CreateFromMessageAsync(OrderPlaced message, CancellationToken ct = default);
}
