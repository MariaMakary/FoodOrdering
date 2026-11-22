using Microsoft.AspNetCore.Identity;
using Shared.Persistence;

namespace Admin.Web.Models.Identity;

public class ApplicationUser : IdentityUser<Guid>, IAuditable
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
}
