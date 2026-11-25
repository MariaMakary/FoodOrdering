using Client.Web.Models.Entities;
using Client.Web.Models.ViewModels;

namespace Client.Web.Services;

public static class CartMapping
{
    public static CartViewModel ToViewModel(Cart? cart)
    {
        if (cart is null)
        {
            return new CartViewModel();
        }

        List<CartItemViewModel> items = cart.Items
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .Select(ToItemViewModel)
            .ToList();

        return new CartViewModel
        {
            Items = items,
            Subtotal = items.Sum(i => i.LineTotal)
        };
    }

    public static bool IsOrderable(MenuItem dish) => dish.IsAvailable && !dish.IsDeleted;

    public static decimal UnitPriceWithExtras(CartItem item) =>
        item.MenuItem.Price + item.Extras.Sum(e => e.MenuItemExtra.Price);

    private static CartItemViewModel ToItemViewModel(CartItem item)
    {
        decimal unitPrice = UnitPriceWithExtras(item);

        return new CartItemViewModel
        {
            CartItemId = item.Id,
            MenuItemId = item.MenuItemId,
            Name = item.MenuItem.Name,
            Extras = item.Extras
                .Select(e => e.MenuItemExtra)
                .OrderBy(e => e.Name)
                .Select(e => new CartItemExtraViewModel { Name = e.Name, Price = e.Price })
                .ToList(),
            Quantity = item.Quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * item.Quantity,
            IsAvailable = IsOrderable(item.MenuItem)
        };
    }
}
