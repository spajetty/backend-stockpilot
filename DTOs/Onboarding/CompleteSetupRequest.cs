using System.ComponentModel.DataAnnotations;
using backend_stockpilot.Models;

namespace backend_stockpilot.DTOs.Onboarding;

public class CompleteSetupRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? MiddleName { get; set; }

    [Required, MaxLength(100)]
    public string Surname { get; set; } = string.Empty;

    [Required]
    public DateOnly? Birthday { get; set; }

    [Required]
    public Gender? Gender { get; set; }

    [Required, MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}