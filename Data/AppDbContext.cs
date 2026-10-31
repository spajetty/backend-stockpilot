using Microsoft.EntityFrameworkCore;
using backend_stockpilot.Models;

namespace backend_stockpilot.Data;

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
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.MiddleName).HasMaxLength(100);
            e.Property(u => u.Surname).HasMaxLength(100);
            e.Property(u => u.Gender).HasConversion<string>().HasMaxLength(20);
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(30);
            e.Property(u => u.GoogleSubject).HasMaxLength(64);
            e.HasIndex(u => u.GoogleSubject).IsUnique(); 
        });
    }
}