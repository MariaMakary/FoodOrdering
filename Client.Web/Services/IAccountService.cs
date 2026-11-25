using System.Security.Claims;
using Client.Web.Models.ViewModels;
using Microsoft.AspNetCore.Identity;

namespace Client.Web.Services;

public interface IAccountService
{
    Task<IdentityResult> RegisterAsync(RegisterViewModel model);
    Task<SignInResult> LoginAsync(LoginViewModel model);
    Task LogoutAsync();
    Task<ProfileViewModel?> GetProfileAsync(ClaimsPrincipal principal);
    Task<IdentityResult> UpdateProfileAsync(ClaimsPrincipal principal, ProfileViewModel model);
    Task<IdentityResult> ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordViewModel model);
}
