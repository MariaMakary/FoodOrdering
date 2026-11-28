using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Client.Web.Models.ViewModels;

public class RateOrderViewModel
{
    public Guid OrderId { get; set; }

    [ValidateNever]
    public string OrderNumber { get; set; } = string.Empty;

    public List<RateDishViewModel> Dishes { get; set; } = [];
}
