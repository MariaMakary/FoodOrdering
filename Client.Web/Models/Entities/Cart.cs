using Client.Web.Models.Identity;
using Shared.Persistence;

namespace Client.Web.Models.Entities;

public class Cart : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }

    public ApplicationUser Customer { get; set; } = null!;
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}
