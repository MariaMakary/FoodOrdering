using Client.Web.Models.ViewModels;

namespace Client.Web.Services;

public static class CartErrors
{
    public static readonly ServiceError DishNotFound =
        new(nameof(DishNotFound), "This dish is no longer on the menu.");

    public static readonly ServiceError DishUnavailable =
        new(nameof(DishUnavailable), "This dish is sold out right now.");

    public static readonly ServiceError InvalidExtras =
        new(nameof(InvalidExtras), "One of the chosen extras is not offered with this dish.");

    public static readonly ServiceError InvalidQuantity =
        new(nameof(InvalidQuantity), $"The quantity must be between 1 and {CartLimits.MaxQuantity}.");

    public static readonly ServiceError CartItemNotFound =
        new(nameof(CartItemNotFound), "This item is no longer in your cart.");
}
