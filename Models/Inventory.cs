namespace backend_stockpilot.Models;

public enum MovementType
{
    Opening,     // imported balance
    Receive,     // goods received from a supplier
    Damage,      // written off
    Missing,     // shrinkage found in a count
    Correction,  // manual count correction
    Return,      // customer return to shelf
    Sale,        // stock out via POS
    Refund       // stock back in via refund
}

public enum ApprovalStatus
{
    NotRequired,
    Pending,
    Approved,
    Rejected
}

public class InventoryLevel
{
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int QuantityOnHand { get; set; }
    public int? ReorderLevelOverride { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class StockMovement
{
    public long Id { get; set; }
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public MovementType MovementType { get; set; }
    public int QuantityChange { get; set; }
    public int? QuantityBefore { get; set; }
    public int? QuantityAfter { get; set; }
    public decimal? UnitCost { get; set; }
    public string? Reason { get; set; }
    public string? Reference { get; set; }   
    public ApprovalStatus Approval { get; set; } = ApprovalStatus.NotRequired;
    public int? ApprovedById { get; set; }
    public User? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}