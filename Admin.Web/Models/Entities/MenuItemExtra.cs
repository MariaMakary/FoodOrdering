using Shared.Persistence;

namespace Admin.Web.Models.Entities;

public class MenuItemExtra : IAuditable
{
    public Guid Id { get; set; }
    public Guid MenuItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public MenuItem MenuItem { get; set; } = null!;
}
