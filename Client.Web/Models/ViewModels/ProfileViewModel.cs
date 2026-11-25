using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Client.Web.Models.ViewModels;

public class ProfileViewModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [BindNever]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Phone]
    [StringLength(30)]
    [Display(Name = "Phone number")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Default delivery address")]
    public string? DefaultAddress { get; set; }
}
