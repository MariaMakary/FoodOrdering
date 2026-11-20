namespace Shared.Contracts.Messages;

public record DishRated : MessageBase
{
    public required Guid RatingId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid MenuItemId { get; init; }
    public required string CustomerName { get; init; }
    public required int Stars { get; init; }
    public string? Comment { get; init; }
    public required DateTime RatedAt { get; init; }
}
