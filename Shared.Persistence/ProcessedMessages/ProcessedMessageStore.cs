using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Shared.Messaging;

namespace Shared.Persistence.ProcessedMessages;

public class ProcessedMessageStore<TContext, TProcessedMessage>(TContext context) : IProcessedMessageStore
    where TContext : DbContext
    where TProcessedMessage : class, IProcessedMessage, new()
{
    public Task<bool> IsProcessedAsync(Guid messageId, CancellationToken ct) =>
        context.Set<TProcessedMessage>()
            .AsNoTracking()
            .AnyAsync(p => EF.Property<Guid>(p, nameof(IProcessedMessage.MessageId)) == messageId, ct);

    public void MarkProcessed(Guid messageId, string messageType) =>
        context.Set<TProcessedMessage>().Add(new TProcessedMessage
        {
            MessageId = messageId,
            Type = messageType,
            ProcessedAt = DateTime.UtcNow
        });

    public bool IsDuplicate(Exception exception)
    {
        if (exception is not DbUpdateException { InnerException: PostgresException postgres }
            || postgres.SqlState != PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }

        IEntityType entityType = context.Model.FindEntityType(typeof(TProcessedMessage))
            ?? throw new InvalidOperationException($"{typeof(TProcessedMessage).Name} is not part of {typeof(TContext).Name}.");

        return postgres.ConstraintName == entityType.FindPrimaryKey()?.GetName()
            && postgres.TableName == entityType.GetTableName();
    }
}
