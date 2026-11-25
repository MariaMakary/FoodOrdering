using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Host), "RabbitMq:Host is missing.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "RabbitMq:Username is missing. Set it in appsettings.Development.json.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Password), "RabbitMq:Password is missing. Set it in appsettings.Development.json.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ExchangeName), "RabbitMq:ExchangeName is missing.")
            .ValidateOnStart();

        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddHostedService<RabbitMqTopologyInitializer>();

        return services;
    }
}
