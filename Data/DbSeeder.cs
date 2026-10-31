using Microsoft.EntityFrameworkCore;
using backend_stockpilot.Models;

namespace backend_stockpilot.Data;

public static class DbSeeder
{
    private const string DemoPassword = "demo1234";

    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync()) return;

        User Make(string email, string first, string surname, UserRole role) => new()
        {
            Email = email,
            FirstName = first,
            Surname = surname,
            Role = role,
            IsFirstLogin = false,
            SetupCompletedAt = DateTime.UtcNow,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DemoPassword),
        };

        db.Users.AddRange(
            Make("owner@stockpilot.ph", "Maria", "Santos", UserRole.Owner),
            Make("manager@stockpilot.ph", "Paolo", "Reyes", UserRole.Manager),
            Make("cashier@stockpilot.ph", "Liza", "Cruz", UserRole.Cashier),
            Make("clerk@stockpilot.ph", "Jun", "Dela Cruz", UserRole.InventoryClerk));

        await db.SaveChangesAsync();
    }
}