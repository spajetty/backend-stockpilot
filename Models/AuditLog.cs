namespace backend_stockpilot.Models;

public enum AuditOutcome
{
    Success,
    Failed
}

public class AuditLog
{
    public long Id { get; set; }

    public int? StoreId { get; set; }
    public Store? Store { get; set; }

    public int? ActorId { get; set; }
    public User? Actor { get; set; }
    public string ActorName { get; set; } = string.Empty;   
    public UserRole ActorRole { get; set; }                 

    public string Action { get; set; } = string.Empty;      // "Product updated", "Refund processed"
    public string Entity { get; set; } = string.Empty;      // "Product" | "Sale" | "Inventory" | "Staff"
    public string? EntityId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public AuditOutcome Outcome { get; set; } = AuditOutcome.Success;

    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}