using Shared.Contracts.Messages;

namespace Client.Web.Services;

public interface IMenuSyncService
{
    Task ApplyMenuItemUpdatedAsync(MenuItemUpdated message, CancellationToken ct = default);
}
