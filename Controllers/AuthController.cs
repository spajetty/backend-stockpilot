using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Auth;
using backend_stockpilot.Models;
using backend_stockpilot.Options;
using backend_stockpilot.Services.Interfaces;

namespace backend_stockpilot.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, ITokenService tokens, IOptions<GoogleOptions> google) : ControllerBase
{
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("not-a-real-password");

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyHash);

        if (user is null || !passwordOk || !user.IsActive)
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(BuildResponse(user));
    }

    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> GoogleLogin(GoogleLoginRequest request)
    {
        var clientId = google.Value.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
            return StatusCode(503, new { message = "Google sign-in isn't configured on the server." });

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                request.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
        }
        catch (InvalidJwtException)
        {
            return Unauthorized(new { message = "Google sign-in failed. Please try again." });
        }

        if (!payload.EmailVerified)
            return Unauthorized(new { message = "Your Google email address isn't verified." });

        var email = payload.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null || !user.IsActive)
            return StatusCode(403, new
            {
                message = "This Google account isn't registered in StockPilot. Ask the owner to add your email address."
            });

        if (user.GoogleSubject is null)
        {
            user.GoogleSubject = payload.Subject;
            await db.SaveChangesAsync();
        }
        else if (user.GoogleSubject != payload.Subject)
        {
            return Unauthorized(new { message = "This email is linked to a different Google account." });
        }

        return Ok(BuildResponse(user));
    }

    [HttpGet("me")]
    [Authorize(Policy = "SetupAllowed")]
    public async Task<ActionResult<UserDto>> Me()
    {
        if (!int.TryParse(User.FindFirst("sub")?.Value, out var id)) return Unauthorized();

        var user = await db.Users.FindAsync(id);
        if (user is null || !user.IsActive) return Unauthorized();

        return UserDto.From(user);
    }

    private LoginResponse BuildResponse(User user)
    {
        var (token, expiresAt) = tokens.CreateToken(user);
        return new LoginResponse(token, expiresAt, UserDto.From(user));
    }
}