using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Persistence;

public static class MigrationExtensions
{
    public static async Task MigrateDatabaseAsync<TContext>(this IHost host, CancellationToken ct = default)
        where TContext : DbContext
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrationExtensions));

        List<string> pending = (await context.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("{Context} is up to date", typeof(TContext).Name);
            return;
        }

        logger.LogInformation("Applying {Count} migrations to {Context}: {Migrations}",
            pending.Count, typeof(TContext).Name, string.Join(", ", pending));
        await context.Database.MigrateAsync(ct);
    }
}
