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
        services.AddScoped<IOrderCancellationService, OrderCancellationService>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IRatingRepository, RatingRepository>();
        services.AddScoped<IRatingService, RatingService>();

        services.AddHostedService<OrderPlacedConsumer>();
        services.AddHostedService<PaymentCompletedConsumer>();
        services.AddHostedService<OrderCancelledConsumer>();
        services.AddHostedService<DishRatedConsumer>();

        return services;
    }
}
