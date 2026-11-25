using Shared.Persistence.ProcessedMessages;

namespace Client.Web.Models.Entities;

public class ProcessedMessage : IProcessedMessage
{
    public Guid MessageId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}
