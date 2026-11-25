using System.Security.Claims;
using Client.Web;
using Client.Web.Data;
using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FoodOrdering.IntegrationTests.Client;

[Collection(PostgresCollection.Name)]
public class AccountServiceTests(PostgresFixture postgres)
{
    private const string Password = "Burger123";
    private const string NewPassword = "Shawarma456";

    [Fact]
    public async Task RegisterAsync_ShouldSaveTheUserWithTheCustomerRoleAndAHashedPassword()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);

        IdentityResult result = await RegisterAsync(services, NewRegistration("sara@example.com"));

        result.Succeeded.ShouldBeTrue();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        ApplicationUser user = await context.Users.SingleAsync(u => u.Email == "sara@example.com");
        user.FirstName.ShouldBe("Sara");
        user.LastName.ShouldBe("Haddad");
        user.PhoneNumber.ShouldBe("+961 70 123 456");
        user.PasswordHash.ShouldNotBeNullOrEmpty();
        user.PasswordHash.ShouldNotContain(Password);

        List<string?> roles = await context.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Join(context.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
            .ToListAsync();
        roles.ShouldBe([ClientRoles.Customer]);
    }

    [Fact]
    public async Task RegisterAsync_ShouldSignTheCustomerIn()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await using AsyncServiceScope scope = NewRequestScope(services, out HttpContext httpContext);

        await scope.ServiceProvider.GetRequiredService<IAccountService>().RegisterAsync(NewRegistration("sara@example.com"));

        httpContext.Response.Headers.SetCookie.ToString().ShouldContain(ServiceExtensions.AuthCookieName);
    }

    [Fact]
    public async Task RegisterAsync_ShouldFail_WhenTheEmailIsAlreadyUsed()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));

        IdentityResult result = await RegisterAsync(services, NewRegistration("SARA@example.com"));

        result.Succeeded.ShouldBeFalse();
        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await context.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task RegisterAsync_ShouldFail_WhenThePasswordIsTooWeak()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        RegisterViewModel registration = NewRegistration("sara@example.com");
        registration.Password = "burgers1";
        registration.ConfirmPassword = "burgers1";

        IdentityResult result = await RegisterAsync(services, registration);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "PasswordRequiresUpper");
    }

    [Fact]
    public async Task LoginAsync_ShouldSucceed_WhenThePasswordIsRight()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));

        SignInResult result = await LoginAsync(services, "sara@example.com", Password);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task LoginAsync_ShouldFail_WhenThePasswordIsWrong()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));

        SignInResult result = await LoginAsync(services, "sara@example.com", "Wrong1234");

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task LoginAsync_ShouldLockTheAccount_WhenThePasswordIsWrongFiveTimes()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await LoginAsync(services, "sara@example.com", "Wrong1234");
        }

        SignInResult result = await LoginAsync(services, "sara@example.com", Password);
        result.IsLockedOut.ShouldBeTrue();
    }

    [Fact]
    public async Task GetProfileAsync_ShouldReturnTheCustomersDetails()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");

        ProfileViewModel? profile = await GetProfileAsync(services, principal);

        profile.ShouldNotBeNull();
        profile.FirstName.ShouldBe("Sara");
        profile.LastName.ShouldBe("Haddad");
        profile.Email.ShouldBe("sara@example.com");
        profile.Phone.ShouldBe("+961 70 123 456");
        profile.DefaultAddress.ShouldBeNull();
    }

    [Fact]
    public async Task GetProfileAsync_ShouldReturnNull_WhenTheUserNoLongerExists()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");
        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            await context.Users.ExecuteDeleteAsync();
        }

        ProfileViewModel? profile = await GetProfileAsync(services, principal);

        profile.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateProfileAsync_ShouldSaveTheNewDetails()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");

        IdentityResult result = await UpdateProfileAsync(services, principal, new ProfileViewModel
        {
            FirstName = " Rana ",
            LastName = "Khoury",
            Phone = "+961 71 999 888",
            DefaultAddress = " Hamra Street 12, Beirut "
        });

        result.Succeeded.ShouldBeTrue();
        ProfileViewModel? reloaded = await GetProfileAsync(services, principal);
        reloaded.ShouldNotBeNull();
        reloaded.FirstName.ShouldBe("Rana");
        reloaded.LastName.ShouldBe("Khoury");
        reloaded.Phone.ShouldBe("+961 71 999 888");
        reloaded.DefaultAddress.ShouldBe("Hamra Street 12, Beirut");
    }

    [Fact]
    public async Task UpdateProfileAsync_ShouldKeepTheEmail_WhenADifferentEmailIsSent()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");
        ProfileViewModel profile = NewProfile();
        profile.Email = "someone.else@example.com";

        await UpdateProfileAsync(services, principal, profile);

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        ApplicationUser user = await context.Users.SingleAsync();
        user.Email.ShouldBe("sara@example.com");
        user.UserName.ShouldBe("sara@example.com");
    }

    [Fact]
    public async Task UpdateProfileAsync_ShouldClearTheDefaultAddress_WhenItIsBlank()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");
        await UpdateProfileAsync(services, principal, NewProfile());
        ProfileViewModel profile = NewProfile();
        profile.DefaultAddress = "   ";

        await UpdateProfileAsync(services, principal, profile);

        await using ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString);
        (await context.Users.SingleAsync()).DefaultAddress.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateProfileAsync_ShouldFail_WhenTheUserNoLongerExists()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");
        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            await context.Users.ExecuteDeleteAsync();
        }

        IdentityResult result = await UpdateProfileAsync(services, principal, NewProfile());

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == AccountErrors.UserNotFound);
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldFail_WhenTheUserNoLongerExists()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");
        await using (ClientDbContext context = postgres.NewDbContext<ClientDbContext>(connectionString))
        {
            await context.Users.ExecuteDeleteAsync();
        }

        IdentityResult result = await ChangePasswordAsync(services, principal, Password, "Pizza1234");

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == AccountErrors.UserNotFound);
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldLetTheCustomerLogInWithTheNewPassword()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");

        IdentityResult result = await ChangePasswordAsync(services, principal, Password, NewPassword);

        result.Succeeded.ShouldBeTrue();
        (await LoginAsync(services, "sara@example.com", NewPassword)).Succeeded.ShouldBeTrue();
        (await LoginAsync(services, "sara@example.com", Password)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldFail_WhenTheCurrentPasswordIsWrong()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");

        IdentityResult result = await ChangePasswordAsync(services, principal, "Wrong1234", NewPassword);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "PasswordMismatch");
        (await LoginAsync(services, "sara@example.com", Password)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldFail_WhenTheNewPasswordIsTooWeak()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        await RegisterAsync(services, NewRegistration("sara@example.com"));
        ClaimsPrincipal principal = await PrincipalForAsync(services, "sara@example.com");

        IdentityResult result = await ChangePasswordAsync(services, principal, Password, "pizzas12");

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "PasswordRequiresUpper");
        (await LoginAsync(services, "sara@example.com", Password)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldKeepTheCustomerSignedIn()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync<ClientDbContext>();
        await using ServiceProvider services = await NewServicesAsync(connectionString);
        string authCookie;
        await using (AsyncServiceScope registerScope = NewRequestScope(services, out HttpContext registerContext))
        {
            await registerScope.ServiceProvider.GetRequiredService<IAccountService>()
                .RegisterAsync(NewRegistration("sara@example.com"));
            authCookie = AuthCookieFrom(registerContext);
        }

        await using AsyncServiceScope scope = NewRequestScope(services, out HttpContext httpContext);
        httpContext.Request.Headers.Cookie = authCookie;
        AuthenticateResult signedIn = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        signedIn.Succeeded.ShouldBeTrue();

        IdentityResult result = await scope.ServiceProvider.GetRequiredService<IAccountService>()
            .ChangePasswordAsync(signedIn.Principal!, new ChangePasswordViewModel
            {
                CurrentPassword = Password,
                NewPassword = NewPassword,
                ConfirmPassword = NewPassword
            });

        result.Succeeded.ShouldBeTrue();
        AuthCookieFrom(httpContext).ShouldNotBe(authCookie);
    }

    private static string AuthCookieFrom(HttpContext httpContext)
    {
        string setCookie = httpContext.Response.Headers.SetCookie.ToString();
        int start = setCookie.IndexOf(ServiceExtensions.AuthCookieName + "=", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        int end = setCookie.IndexOf(';', start);
        return end < 0 ? setCookie[start..] : setCookie[start..end];
    }

    private static async Task<ClaimsPrincipal> PrincipalForAsync(ServiceProvider services, string email)
    {
        await using AsyncServiceScope scope = NewRequestScope(services, out _);
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = (await users.FindByEmailAsync(email))!;
        return await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
    }

    private static async Task<ProfileViewModel?> GetProfileAsync(ServiceProvider services, ClaimsPrincipal principal)
    {
        await using AsyncServiceScope scope = NewRequestScope(services, out _);
        return await scope.ServiceProvider.GetRequiredService<IAccountService>().GetProfileAsync(principal);
    }

    private static async Task<IdentityResult> UpdateProfileAsync(
        ServiceProvider services, ClaimsPrincipal principal, ProfileViewModel profile)
    {
        await using AsyncServiceScope scope = NewRequestScope(services, out _);
        return await scope.ServiceProvider.GetRequiredService<IAccountService>().UpdateProfileAsync(principal, profile);
    }

    private static async Task<IdentityResult> ChangePasswordAsync(
        ServiceProvider services, ClaimsPrincipal principal, string currentPassword, string newPassword)
    {
        await using AsyncServiceScope scope = NewRequestScope(services, out _);
        return await scope.ServiceProvider.GetRequiredService<IAccountService>()
            .ChangePasswordAsync(principal, new ChangePasswordViewModel
            {
                CurrentPassword = currentPassword,
                NewPassword = newPassword,
                ConfirmPassword = newPassword
            });
    }

    private static ProfileViewModel NewProfile() => new()
    {
        FirstName = "Sara",
        LastName = "Haddad",
        Phone = "+961 70 123 456",
        DefaultAddress = "Hamra Street 12, Beirut"
    };

    private static async Task<ServiceProvider> NewServicesAsync(string connectionString)
    {
        ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<ClientDbContext>(options => options.UseNpgsql(connectionString))
            .AddClientIdentity()
            .AddClientServices()
            .BuildServiceProvider();

        await IdentitySeeder.SeedRolesAsync(services);
        return services;
    }

    private static AsyncServiceScope NewRequestScope(ServiceProvider services, out HttpContext httpContext)
    {
        AsyncServiceScope scope = services.CreateAsyncScope();
        httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;
        return scope;
    }

    private static async Task<IdentityResult> RegisterAsync(ServiceProvider services, RegisterViewModel registration)
    {
        await using AsyncServiceScope scope = NewRequestScope(services, out _);
        return await scope.ServiceProvider.GetRequiredService<IAccountService>().RegisterAsync(registration);
    }

    private static async Task<SignInResult> LoginAsync(ServiceProvider services, string email, string password)
    {
        await using AsyncServiceScope scope = NewRequestScope(services, out _);
        return await scope.ServiceProvider.GetRequiredService<IAccountService>()
            .LoginAsync(new LoginViewModel { Email = email, Password = password });
    }

    private static RegisterViewModel NewRegistration(string email) => new()
    {
        FirstName = "Sara",
        LastName = "Haddad",
        Email = email,
        Phone = "+961 70 123 456",
        Password = Password,
        ConfirmPassword = Password
    };
}
