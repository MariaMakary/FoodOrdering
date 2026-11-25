using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Client.Web.Controllers;

public class AccountController(IAccountService accounts) : Controller
{
    public const string SuccessKey = "Success";

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToHome();
        }

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        IdentityResult result = await accounts.RegisterAsync(model);
        if (result.Succeeded)
        {
            return RedirectToHome();
        }

        AddErrors(result);
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToHome();
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        SignInResult result = await accounts.LoginAsync(model);
        if (result.Succeeded)
        {
            if (model.ReturnUrl is not null && Url.IsLocalUrl(model.ReturnUrl))
            {
                return LocalRedirect(model.ReturnUrl);
            }

            return RedirectToHome();
        }

        ModelState.AddModelError(
            string.Empty,
            result.IsLockedOut ? "Too many failed attempts. Try again in 5 minutes." : "Wrong email or password.");

        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = ClientRoles.Customer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await accounts.LogoutAsync();
        return RedirectToHome();
    }

    [HttpGet]
    [Authorize(Roles = ClientRoles.Customer)]
    public async Task<IActionResult> Profile()
    {
        ProfileViewModel? profile = await accounts.GetProfileAsync(User);
        if (profile is null)
        {
            return await SignOutMissingUserAsync();
        }

        return View(profile);
    }

    [HttpPost]
    [Authorize(Roles = ClientRoles.Customer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        if (ModelState.IsValid)
        {
            IdentityResult result = await accounts.UpdateProfileAsync(User, model);
            if (result.Succeeded)
            {
                TempData[SuccessKey] = "Your profile was saved.";
                return RedirectToAction(nameof(Profile));
            }

            AddErrors(result);
        }

        ProfileViewModel? current = await accounts.GetProfileAsync(User);
        if (current is null)
        {
            return await SignOutMissingUserAsync();
        }

        model.Email = current.Email;
        return View(model);
    }

    [HttpGet]
    [Authorize(Roles = ClientRoles.Customer)]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    [Authorize(Roles = ClientRoles.Customer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        IdentityResult result = await accounts.ChangePasswordAsync(User, model);
        if (result.Succeeded)
        {
            TempData[SuccessKey] = "Your password was changed.";
            return RedirectToAction(nameof(ChangePassword));
        }

        if (result.Errors.Any(e => e.Code == AccountErrors.UserNotFound))
        {
            return await SignOutMissingUserAsync();
        }

        AddErrors(result);
        return View(new ChangePasswordViewModel());
    }

    private void AddErrors(IdentityResult result)
    {
        foreach (IdentityError error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }

    private async Task<IActionResult> SignOutMissingUserAsync()
    {
        await accounts.LogoutAsync();
        return RedirectToAction(nameof(Login));
    }

    private RedirectToActionResult RedirectToHome() =>
        RedirectToAction(nameof(HomeController.Index), "Home");
}
