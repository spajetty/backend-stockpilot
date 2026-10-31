using System.ComponentModel.DataAnnotations;

namespace backend_stockpilot.Dtos.Auth;

public class GoogleLoginRequest
{
    [Required]
    public string IdToken { get; set; } = string.Empty;
}