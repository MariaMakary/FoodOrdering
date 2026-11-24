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
}
