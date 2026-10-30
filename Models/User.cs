namespace backend_stockpilot.Models
{
    public enum UserRole
    {
        Owner,
        Manager,
        Cashier,
        InventoryClerk
    }

    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty; // BCrypt hash, never the plain password
        public string FullName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}