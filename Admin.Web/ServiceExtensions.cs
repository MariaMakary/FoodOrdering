using Admin.Web.Messaging.Consumers;
using Admin.Web.Repositories;
using Admin.Web.Services;

namespace Admin.Web;

public static class ServiceExtensions
{
    public static IServiceCollection AddAdminServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderService, OrderService>();

        services.AddHostedService<OrderPlacedConsumer>();

        return services;
    }
}
