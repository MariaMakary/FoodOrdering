using Client.Web.Data;
using Client.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Client.Web.Repositories;

public class CartRepository(ClientDbContext context) : ICartRepository
{
    public Task<Cart?> GetByCustomerAsync(Guid customerId, CancellationToken ct = default) =>
        context.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.MenuItem)
            .Include(c => c.Items)
                .ThenInclude(i => i.Extras)
                .ThenInclude(e => e.MenuItemExtra)
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.CustomerId == customerId, ct);

    public Task<CartItem?> GetItemAsync(Guid customerId, Guid cartItemId, CancellationToken ct = default) =>
        context.CartItems
            .SingleOrDefaultAsync(i => i.Id == cartItemId && i.Cart.CustomerId == customerId, ct);

    public void Add(Cart cart) => context.Carts.Add(cart);

    public void AddItem(CartItem item) => context.CartItems.Add(item);

    public void RemoveItem(CartItem item) => context.CartItems.Remove(item);

    public void ClearItems(Cart cart) => context.CartItems.RemoveRange(cart.Items);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
