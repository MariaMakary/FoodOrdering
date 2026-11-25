using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Shared.Contracts.Enums;

namespace Client.Web.Models.ViewModels;

public class CheckoutViewModel : IValidatableObject
{
    [EnumDataType(typeof(DeliveryType))]
    [Display(Name = "Delivery or pickup")]
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Delivery;

    [StringLength(500)]
    [Display(Name = "Delivery address")]
    public string? Address { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notes for the restaurant")]
    public string? Notes { get; set; }

    [EnumDataType(typeof(PaymentMethod))]
    [Display(Name = "Payment method")]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Online;

    [BindNever]
    [ValidateNever]
    public CartViewModel Cart { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DeliveryType == DeliveryType.Delivery && string.IsNullOrWhiteSpace(Address))
        {
            yield return new ValidationResult("Enter a delivery address, or choose pickup.", [nameof(Address)]);
        }
    }
}
