using System.Security.Claims;

namespace Client.Web.Controllers;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetCustomerId(this ClaimsPrincipal user)
    {
        string? id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out Guid customerId)
            ? customerId
            : throw new InvalidOperationException("The signed-in user has no valid id claim.");
    }
}
