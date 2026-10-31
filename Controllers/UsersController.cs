using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Users;
using backend_stockpilot.Models;
using backend_stockpilot.Services;
using backend_stockpilot.Services.Interfaces;

namespace backend_stockpilot.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Owner")]
public class UsersController(AppDbContext db, IEmailService email, ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserSummaryDto>>> List(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderByDescending(u => u.CreatedAt).ToListAsync(ct);
        return users.Select(UserSummaryDto.From).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<UserSummaryDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var role = request.Role!.Value;
        if (role == UserRole.Owner)
            return BadRequest(new { message = "Owner accounts can't be created from here." });

        var address = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == address, ct))
            return Conflict(new { message = "An account with this email already exists." });

        var temporaryPassword = PasswordPolicy.GenerateTemporary();
        var user = new User
        {
            Email = address,
            Role = role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword),
            IsFirstLogin = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        try
        {
            var invitedBy = User.FindFirst("name")?.Value ?? "The owner";
            await email.SendInvitationAsync(address, role.ToString(), temporaryPassword, invitedBy, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send invitation email to {Email}", address);
            db.Users.Remove(user);
            await db.SaveChangesAsync(CancellationToken.None);
            return StatusCode(502, new
            {
                message = "We couldn't send the email, so the account wasn't created. Check the email settings and try again."
            });
        }

        return StatusCode(StatusCodes.Status201Created, UserSummaryDto.From(user));
    }
}