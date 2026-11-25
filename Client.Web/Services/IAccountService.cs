using Client.Web.Models.ViewModels;
using Microsoft.AspNetCore.Identity;

namespace Client.Web.Services;

public interface IAccountService
{
    Task<IdentityResult> RegisterAsync(RegisterViewModel model);
    Task<SignInResult> LoginAsync(LoginViewModel model);
    Task LogoutAsync();
}
