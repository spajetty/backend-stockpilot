using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Auth;
using backend_stockpilot.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend_stockpilot.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, ITokenService tokens) : ControllerBase
{
    // Verified when the email doesn't exist, so response time doesn't reveal which emails are real
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("not-a-real-password");

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyHash);

        // One message for unknown email, wrong password, and disabled account
        if (user is null || !passwordOk || !user.IsActive)
            return Unauthorized(new { message = "Invalid email or password." });

        var (token, expiresAt) = tokens.CreateToken(user);
        return Ok(new LoginResponse(token, expiresAt, UserDto.From(user)));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        if (!int.TryParse(User.FindFirst("sub")?.Value, out var id)) return Unauthorized();

        var user = await db.Users.FindAsync(id);
        if (user is null || !user.IsActive) return Unauthorized();

        return UserDto.From(user);
    }
}