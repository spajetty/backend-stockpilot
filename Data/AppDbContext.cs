using Microsoft.EntityFrameworkCore;
using backend_stockpilot.Models;

namespace backend_stockpilot.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(e =>
            {
                e.HasIndex(u => u.Email).IsUnique();
                e.Property(u => u.Email).HasMaxLength(255).IsRequired();
                e.Property(u => u.PasswordHash).IsRequired();
                e.Property(u => u.FullName).HasMaxLength(150).IsRequired();
                // Stored as text ("InventoryClerk"), not 0-3, so it matches the frontend's Role type
                e.Property(u => u.Role).HasConversion<string>().HasMaxLength(30);
            });
        }
    }
}
