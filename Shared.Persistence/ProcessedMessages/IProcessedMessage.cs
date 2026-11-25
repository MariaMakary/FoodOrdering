namespace Shared.Persistence.ProcessedMessages;

public interface IProcessedMessage
{
    Guid MessageId { get; set; }
    string Type { get; set; }
    DateTime ProcessedAt { get; set; }
}
