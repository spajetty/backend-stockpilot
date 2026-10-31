using System.ComponentModel.DataAnnotations;
using backend_stockpilot.Models;

namespace backend_stockpilot.Dtos.Users;

public class CreateUserRequest
{
    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public UserRole? Role { get; set; }
}