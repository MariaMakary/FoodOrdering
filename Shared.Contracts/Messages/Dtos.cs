namespace Shared.Contracts.Messages;

public record ExtraDto
{
    public required string Name { get; init; }
    public required decimal Price { get; init; }
}

public record OrderItemDto
{
    public required Guid MenuItemId { get; init; }
    public required string Name { get; init; }
    public required decimal UnitPrice { get; init; }
    public required int Quantity { get; init; }
    public IReadOnlyList<ExtraDto> Extras { get; init; } = [];
}

public record CategoryDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int SortOrder { get; init; }
}

public record MenuItemExtraDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required decimal Price { get; init; }
}
