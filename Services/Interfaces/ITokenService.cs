using backend_stockpilot.Models;

namespace backend_stockpilot.Services.Interfaces;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(User user);
}