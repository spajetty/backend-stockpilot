using backend_stockpilot.Models;

namespace backend_stockpilot.Dtos.Auth;

public record UserDto(int Id, string Email, string FullName, string Role, bool IsFirstLogin)
{
    public static UserDto From(User user) =>
        new(user.Id, user.Email, user.DisplayName, user.Role.ToString(), user.IsFirstLogin);
}