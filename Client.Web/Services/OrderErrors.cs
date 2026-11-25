namespace Client.Web.Services;

public static class OrderErrors
{
    public const string UnavailableDishesCode = "UnavailableDishes";

    public static readonly ServiceError EmptyCart =
        new(nameof(EmptyCart), "Your cart is empty. Add a dish before checking out.");

    public static readonly ServiceError AddressRequired =
        new(nameof(AddressRequired), "Enter a delivery address, or choose pickup.");

    public static readonly ServiceError CustomerNotFound =
        new(nameof(CustomerNotFound), "Your account was not found. Please log in again.");

    public static readonly ServiceError OrderNotFound =
        new(nameof(OrderNotFound), "This order was not found.");

    public static readonly ServiceError CannotCancel =
        new(nameof(CannotCancel), "This order can no longer be cancelled, because the restaurant has already handled it.");

    public static readonly ServiceError ChangedByRestaurant =
        new(nameof(ChangedByRestaurant), "The restaurant has just updated your order, so it was not cancelled. Please check its new status.");

    public static ServiceError UnavailableDishes(IEnumerable<string> dishNames) =>
        new(UnavailableDishesCode,
            $"These dishes are no longer available: {string.Join(", ", dishNames)}. Remove them from your cart to continue.");
}
