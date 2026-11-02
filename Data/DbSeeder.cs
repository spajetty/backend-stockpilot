
using Microsoft.EntityFrameworkCore;
using backend_stockpilot.Models;

namespace backend_stockpilot.Data;

public static class DbSeeder
{
    private const string DemoPassword = "demo1234";

    public static async Task SeedAsync(AppDbContext db)
    {
        if (!await db.Stores.AnyAsync())
        {
            db.Stores.AddRange(
                new Store { Code = "MNL", Name = "StockPilot Demo Store" },
                new Store { Code = "QC", Name = "StockPilot Quezon City" },
                new Store { Code = "CEB", Name = "StockPilot Cebu" }
            );

            await db.SaveChangesAsync();
        }

        var demoStore = await db.Stores.FirstAsync(s => s.Code == "MNL");

        if (await db.Users.AnyAsync()) return;

        User Make(string email, string first, string surname, UserRole role, int? storeId = null) => new()
        {
            Email = email,
            FirstName = first,
            Surname = surname,
            Role = role,
            StoreId = storeId,
            IsFirstLogin = false,
            SetupCompletedAt = DateTime.UtcNow,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DemoPassword),
        };

        db.Users.AddRange(
            Make("owner@stockpilot.ph", "Maria", "Santos", UserRole.Owner),
            Make("manager@stockpilot.ph", "Paolo", "Reyes", UserRole.Manager, demoStore.Id),
            Make("cashier@stockpilot.ph", "Liza", "Cruz", UserRole.Cashier, demoStore.Id),
            Make("clerk@stockpilot.ph", "Jun", "Dela Cruz", UserRole.InventoryClerk, demoStore.Id)
        );

        await db.SaveChangesAsync();
    }
}
