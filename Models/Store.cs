namespace backend_stockpilot.Models;

public class Store
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string Currency { get; set; } = "PHP";
    public decimal TaxRate { get; set; } = 12.00m;
    public string? ReceiptFooter { get; set; }
    public bool LowStockAlerts { get; set; } = true;
    public int DefaultReorderLevel { get; set; } = 20;

    public string TimeZone { get; set; } = "Asia/Manila";
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();
}