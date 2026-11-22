using Admin.Web.Repositories;
using Admin.Web.Services;

namespace Admin.Web;

public static class ServiceExtensions
{
    public static IServiceCollection AddAdminServices(this IServiceCollection services)
    {
        services.AddScoped<IExampleRepository, ExampleRepository>();
        services.AddScoped<IExampleService, ExampleService>();

        return services;
    }
}
