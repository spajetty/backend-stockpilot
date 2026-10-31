using backend_stockpilot.Models;

namespace backend_stockpilot.Dtos.Users;

public record UserSummaryDto(
    int Id, string Email, string FullName, string Role, bool IsFirstLogin, bool IsActive, DateTime CreatedAt)
{
    public static UserSummaryDto From(User u) =>
        new(u.Id, u.Email, u.DisplayName, u.Role.ToString(), u.IsFirstLogin, u.IsActive, u.CreatedAt);
}