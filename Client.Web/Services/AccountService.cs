using System.Security.Claims;
using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Microsoft.AspNetCore.Identity;

namespace Client.Web.Services;

public class AccountService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : IAccountService
{
    public async Task<IdentityResult> RegisterAsync(RegisterViewModel model)
    {
        string email = model.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            PhoneNumber = model.Phone.Trim(),
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim()
        };

        IdentityResult created = await users.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            return created;
        }

        IdentityResult addedToRole = await users.AddToRoleAsync(user, ClientRoles.Customer);
        if (!addedToRole.Succeeded)
        {
            await users.DeleteAsync(user);
            return addedToRole;
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return IdentityResult.Success;
    }

    public Task<SignInResult> LoginAsync(LoginViewModel model) =>
        signIn.PasswordSignInAsync(model.Email.Trim(), model.Password, model.RememberMe, lockoutOnFailure: true);

    public Task LogoutAsync() => signIn.SignOutAsync();

    public async Task<ProfileViewModel?> GetProfileAsync(ClaimsPrincipal principal)
    {
        ApplicationUser? user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return null;
        }

        return new ProfileViewModel
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email ?? string.Empty,
            Phone = user.PhoneNumber ?? string.Empty,
            DefaultAddress = user.DefaultAddress
        };
    }

    public async Task<IdentityResult> UpdateProfileAsync(ClaimsPrincipal principal, ProfileViewModel model)
    {
        ApplicationUser? user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return UserNotFound();
        }

        user.FirstName = model.FirstName.Trim();
        user.LastName = model.LastName.Trim();
        user.PhoneNumber = model.Phone.Trim();
        user.DefaultAddress = string.IsNullOrWhiteSpace(model.DefaultAddress) ? null : model.DefaultAddress.Trim();

        return await users.UpdateAsync(user);
    }

    public async Task<IdentityResult> ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordViewModel model)
    {
        ApplicationUser? user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return UserNotFound();
        }

        IdentityResult changed = await users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (changed.Succeeded)
        {
            await signIn.RefreshSignInAsync(user);
        }

        return changed;
    }

    private static IdentityResult UserNotFound() =>
        IdentityResult.Failed(new IdentityError
        {
            Code = AccountErrors.UserNotFound,
            Description = "Your account was not found. Please log in again."
        });
}
