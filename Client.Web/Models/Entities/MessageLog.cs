using Client.Web.Models.Enums;

namespace Client.Web.Models.Entities;

public class MessageLog
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string Type { get; set; } = string.Empty;
    public MessageDirection Direction { get; set; }
    public MessageLogStatus Status { get; set; }
    public string Queue { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
