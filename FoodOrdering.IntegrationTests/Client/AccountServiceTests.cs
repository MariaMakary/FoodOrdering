using Client.Web;
using Client.Web.Data;
using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Services;
using FoodOrdering.IntegrationTests.Infrastructure;
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
