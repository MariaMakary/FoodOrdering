namespace Admin.Web.Models.Entities;

public class MenuItem
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? PhotoUrl { get; set; }
    public bool IsVegetarian { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
    public ICollection<MenuItemExtra> Extras { get; set; } = new List<MenuItemExtra>();
}
