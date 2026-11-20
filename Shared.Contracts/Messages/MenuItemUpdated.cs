using Shared.Contracts.Enums;

namespace Shared.Contracts.Messages;

public record MenuItemUpdated : MessageBase
{
    public required MenuItemAction Action { get; init; }
    public required Guid MenuItemId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public decimal? Price { get; init; }
    public string? PhotoUrl { get; init; }
    public bool IsVegetarian { get; init; }
    public bool IsAvailable { get; init; }
    public CategoryDto? Category { get; init; }
    public IReadOnlyList<MenuItemExtraDto> Extras { get; init; } = [];
    public required DateTime UpdatedAt { get; init; }
}
