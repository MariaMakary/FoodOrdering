using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Contracts.Messages;
using Shared.Messaging;

namespace Shared.Persistence.Outbox;

public class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        do
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox: dispatching failed, trying again in {Interval}", options.Value.PollInterval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> DispatchPendingAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();

        List<OutboxMessage> pending = await context.Set<OutboxMessage>()
            .Where(m => m.SentAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(options.Value.BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return 0;
        }

        IMessagePublisher publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();
        int sent = 0;

        foreach (OutboxMessage row in pending)
        {
            try
            {
                MessageBase message = row.ToMessage();
                await publisher.PublishAsync(message, ct);
                row.SentAt = DateTime.UtcNow;
                row.LastError = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.Attempts++;
                row.LastError = ex.Message;
                logger.LogWarning(ex, "Outbox: could not publish {MessageType} {MessageId}, attempt {Attempts}",
                    row.Type, row.Id, row.Attempts);
                await context.SaveChangesAsync(ct);
                break;
            }

            await context.SaveChangesAsync(ct);
        }

        return sent;
    }
}
