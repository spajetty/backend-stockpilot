namespace backend_stockpilot.Dtos.Auth;

public record LoginResponse(string Token, DateTime ExpiresAt, UserDto User);