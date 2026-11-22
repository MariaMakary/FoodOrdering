namespace Client.Web.Models.Entities;

public class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}
