namespace Client.Web.Services;

public static class RatingErrors
{
    public static readonly ServiceError OrderNotFound =
        new(nameof(OrderNotFound), "This order was not found, or it has not been delivered yet.");

    public static readonly ServiceError AlreadyRated =
        new(nameof(AlreadyRated), "You have already rated these dishes from this order.");

    public static readonly ServiceError InvalidDishes =
        new(nameof(InvalidDishes), "Rate only the dishes from this order that you have not rated yet.");

    public static readonly ServiceError InvalidStars =
        new(nameof(InvalidStars), "Choose between 1 and 5 stars for each dish you rate.");

    public static readonly ServiceError NothingRated =
        new(nameof(NothingRated), "Choose stars for at least one dish.");

    public static readonly ServiceError CustomerNotFound =
        new(nameof(CustomerNotFound), "Your account was not found. Please log in again.");
}
