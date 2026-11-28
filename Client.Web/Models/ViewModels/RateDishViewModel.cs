using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Client.Web.Models.ViewModels;

public class RateDishViewModel
{
    public const int MinStars = 1;
    public const int MaxStars = 5;
    public const int MaxCommentLength = 1000;

    public Guid MenuItemId { get; set; }

    [ValidateNever]
    public string Name { get; set; } = string.Empty;

    [ValidateNever]
    public string? PhotoUrl { get; set; }

    [Range(MinStars, MaxStars, ErrorMessage = "Choose between 1 and 5 stars.")]
    [Display(Name = "Stars")]
    public int? Stars { get; set; }

    [StringLength(MaxCommentLength)]
    [Display(Name = "Comment")]
    public string? Comment { get; set; }
}
