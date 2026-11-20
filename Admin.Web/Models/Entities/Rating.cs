namespace Admin.Web.Models.Entities;

public class Rating
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid MenuItemId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int Stars { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }

    public Order Order { get; set; } = null!;
    public MenuItem MenuItem { get; set; } = null!;
}
