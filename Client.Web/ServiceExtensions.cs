using Client.Web.Repositories;
using Client.Web.Services;

namespace Client.Web;

public static class ServiceExtensions
{
    public static IServiceCollection AddClientServices(this IServiceCollection services)
    {
        services.AddScoped<IExampleRepository, ExampleRepository>();
        services.AddScoped<IExampleService, ExampleService>();

        return services;
    }
}
