using Client.Web.Models.ViewModels;

namespace Client.Web.Services;

public interface ICartService
{
    Task<CartViewModel> GetCartAsync(Guid customerId, CancellationToken ct = default);

    Task<ServiceResult> AddItemAsync(Guid customerId, AddToCartViewModel model, CancellationToken ct = default);

    Task<ServiceResult> UpdateQuantityAsync(Guid customerId, UpdateCartItemViewModel model, CancellationToken ct = default);

    Task<ServiceResult> RemoveItemAsync(Guid customerId, Guid cartItemId, CancellationToken ct = default);

    Task ClearAsync(Guid customerId, CancellationToken ct = default);
}
