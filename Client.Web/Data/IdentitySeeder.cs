using Client.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace Client.Web.Data;

public static class IdentitySeeder
{
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        RoleManager<ApplicationRole> roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        if (await roles.RoleExistsAsync(ClientRoles.Customer))
        {
            return;
        }

        IdentityResult result = await roles.CreateAsync(new ApplicationRole { Name = ClientRoles.Customer });
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the {ClientRoles.Customer} role: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }
}
