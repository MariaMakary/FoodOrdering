using Client.Web.Models.Entities;

namespace Client.Web.Repositories;

public interface ICartRepository
{
    Task<Cart?> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);

    Task<CartItem?> GetItemAsync(Guid customerId, Guid cartItemId, CancellationToken ct = default);

    void Add(Cart cart);

    void AddItem(CartItem item);

    void RemoveItem(CartItem item);

    void ClearItems(Cart cart);

    Task SaveChangesAsync(CancellationToken ct = default);
}
