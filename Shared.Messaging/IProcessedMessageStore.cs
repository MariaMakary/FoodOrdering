namespace Shared.Messaging;

public interface IProcessedMessageStore
{
    Task<bool> IsProcessedAsync(Guid messageId, CancellationToken ct);

    void MarkProcessed(Guid messageId, string messageType);

    bool IsDuplicate(Exception exception);
}
