using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Persistence.Outbox;

public static class OutboxExtensions
{
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        return modelBuilder;
    }

    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName);
        services.AddScoped<IOutbox, EfOutbox<TContext>>();
        services.AddSingleton<OutboxDispatcher<TContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher<TContext>>());

        return services;
    }
}
