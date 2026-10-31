using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Auth;
using backend_stockpilot.Services;
using backend_stockpilot.DTOs.Onboarding;

namespace backend_stockpilot.Controllers;

[ApiController]
[Route("api/onboarding")]
[Authorize(Policy = "SetupAllowed")]
public class OnboardingController(AppDbContext db) : ControllerBase
{
    [HttpPost("complete")]
    public async Task<ActionResult<UserDto>> Complete(CompleteSetupRequest request)
    {
        if (!int.TryParse(User.FindFirst("sub")?.Value, out var id)) return Unauthorized();

        var user = await db.Users.FindAsync(id);
        if (user is null || !user.IsActive) return Unauthorized();
        if (!user.IsFirstLogin) return Conflict(new { message = "Account setup is already complete." });

        var birthday = request.Birthday!.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (birthday > today || birthday < today.AddYears(-120))
            return BadRequest(new { message = "Enter a valid birthday." });

        var passwordError = PasswordPolicy.Validate(request.NewPassword);
        if (passwordError is not null) return BadRequest(new { message = passwordError });

        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
            return BadRequest(new { message = "Choose a password different from your temporary one." });

        user.FirstName = request.FirstName.Trim();
        user.MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim();
        user.Surname = request.Surname.Trim();
        user.Birthday = birthday;
        user.Gender = request.Gender;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.IsFirstLogin = false;
        user.SetupCompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return UserDto.From(user);
    }
}