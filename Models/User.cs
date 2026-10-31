namespace backend_stockpilot.Models;

public enum UserRole
{
    Owner,
    Manager,
    Cashier,
    InventoryClerk
}

public enum Gender
{
    Male,
    Female,
    Other,
    PreferNotToSay
}

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? Surname { get; set; }
    public DateOnly? Birthday { get; set; }
    public Gender? Gender { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFirstLogin { get; set; } = true;
    public DateTime? SetupCompletedAt { get; set; }
    public string? GoogleSubject { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Get-only, so EF Core doesn't map it to a column
    public string DisplayName =>
        string.IsNullOrWhiteSpace(FirstName) ? Email : $"{FirstName} {Surname}".Trim();
}