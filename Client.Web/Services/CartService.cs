using Client.Web.Models.Entities;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;

namespace Client.Web.Services;

public class CartService(ICartRepository carts, IMenuRepository menu) : ICartService
{
    public async Task<CartViewModel> GetCartAsync(Guid customerId, CancellationToken ct = default)
    {
        Cart? cart = await carts.GetByCustomerAsync(customerId, ct);
        return CartMapping.ToViewModel(cart);
    }

    public async Task<ServiceResult> AddItemAsync(Guid customerId, AddToCartViewModel model, CancellationToken ct = default)
    {
        if (!IsValidQuantity(model.Quantity))
        {
            return ServiceResult.Failure(CartErrors.InvalidQuantity);
        }

        MenuItem? dish = await menu.GetByIdAsync(model.MenuItemId, ct);
        if (dish is null)
        {
            return ServiceResult.Failure(CartErrors.DishNotFound);
        }

        if (!CartMapping.IsOrderable(dish))
        {
            return ServiceResult.Failure(CartErrors.DishUnavailable);
        }

        HashSet<Guid> extraIds = model.ExtraIds.ToHashSet();
        HashSet<Guid> offeredExtraIds = dish.Extras.Select(e => e.Id).ToHashSet();
        if (!extraIds.IsSubsetOf(offeredExtraIds))
        {
            return ServiceResult.Failure(CartErrors.InvalidExtras);
        }

        Cart? cart = await carts.GetByCustomerAsync(customerId, ct);
        if (cart is null)
        {
            cart = new Cart { Id = Guid.NewGuid(), CustomerId = customerId };
            carts.Add(cart);
        }

        CartItem? sameLine = cart.Items.FirstOrDefault(i =>
            i.MenuItemId == dish.Id && extraIds.SetEquals(i.Extras.Select(e => e.MenuItemExtraId)));

        if (sameLine is not null)
        {
            sameLine.Quantity = Math.Min(sameLine.Quantity + model.Quantity, CartLimits.MaxQuantity);
        }
        else
        {
            Guid cartItemId = Guid.NewGuid();
            carts.AddItem(new CartItem
            {
                Id = cartItemId,
                CartId = cart.Id,
                MenuItemId = dish.Id,
                Quantity = model.Quantity,
                Extras = extraIds
                    .Select(extraId => new CartItemExtra { CartItemId = cartItemId, MenuItemExtraId = extraId })
                    .ToList()
            });
        }

        await carts.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UpdateQuantityAsync(
        Guid customerId,
        UpdateCartItemViewModel model,
        CancellationToken ct = default)
    {
        if (!IsValidQuantity(model.Quantity))
        {
            return ServiceResult.Failure(CartErrors.InvalidQuantity);
        }

        CartItem? item = await carts.GetItemAsync(customerId, model.CartItemId, ct);
        if (item is null)
        {
            return ServiceResult.Failure(CartErrors.CartItemNotFound);
        }

        item.Quantity = model.Quantity;
        await carts.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RemoveItemAsync(Guid customerId, Guid cartItemId, CancellationToken ct = default)
    {
        CartItem? item = await carts.GetItemAsync(customerId, cartItemId, ct);
        if (item is null)
        {
            return ServiceResult.Failure(CartErrors.CartItemNotFound);
        }

        carts.RemoveItem(item);
        await carts.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task ClearAsync(Guid customerId, CancellationToken ct = default)
    {
        Cart? cart = await carts.GetByCustomerAsync(customerId, ct);
        if (cart is null || cart.Items.Count == 0)
        {
            return;
        }

        carts.ClearItems(cart);
        await carts.SaveChangesAsync(ct);
    }

    private static bool IsValidQuantity(int quantity) =>
        quantity is >= CartLimits.MinQuantity and <= CartLimits.MaxQuantity;
}
