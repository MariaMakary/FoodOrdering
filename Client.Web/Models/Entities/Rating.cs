using Client.Web.Models.Identity;
using Shared.Persistence;

namespace Client.Web.Models.Entities;

public class Rating : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid MenuItemId { get; set; }
    public Guid CustomerId { get; set; }
    public int Stars { get; set; }
    public string? Comment { get; set; }

    public Order Order { get; set; } = null!;
    public MenuItem MenuItem { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;
}
