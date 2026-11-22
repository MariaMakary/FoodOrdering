using Shared.Persistence;

namespace Client.Web.Models.Entities;

public class MenuItemExtra : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid MenuItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    public MenuItem MenuItem { get; set; } = null!;
}
