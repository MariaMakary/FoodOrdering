using Client.Web.Data;
using Client.Web.Models.Identity;
using Client.Web.Services;
using Microsoft.AspNetCore.Identity;

namespace Client.Web;

public static class ServiceExtensions
{
    public const string AuthCookieName = "FoodOrdering.Client.Auth";

    public static IServiceCollection AddClientIdentity(this IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<ClientDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = AuthCookieName;
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
        });

        return services;
    }

    public static IServiceCollection AddClientServices(this IServiceCollection services)
    {
        services.AddScoped<IAccountService, AccountService>();

        return services;
    }
}
