using System.ComponentModel.DataAnnotations;

namespace Client.Web.Models.ViewModels;

public class MenuItemExtraViewModel
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [DataType(DataType.Currency)]
    public decimal Price { get; set; }
}
