namespace backend_stockpilot.Models;

// One enum for both the sale's payment method and the gateway provider
public enum PaymentMethod
{
    Cash,
    Card,
    PayPal
}

public enum SalePaymentStatus
{
    Paid,
    PartiallyRefunded,
    Refunded
}

public enum SaleStatus
{
    Completed,
    Voided
}

public class Sale
{
    public int Id { get; set; }

    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public string SaleNumber { get; set; } = string.Empty;   // SP-MNL-261102-0001

    public int? CashierId { get; set; }
    public User? Cashier { get; set; }
    public string CashierName { get; set; } = string.Empty;  

    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public SalePaymentStatus PaymentStatus { get; set; } = SalePaymentStatus.Paid;
    public SaleStatus Status { get; set; } = SaleStatus.Completed;

    public decimal? CashTendered { get; set; }
    public decimal? ChangeDue { get; set; }

    public int? VoidedById { get; set; }
    public User? VoidedBy { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

public class SaleItem
{
    public int Id { get; set; }

    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }    // for margin reports
    public decimal LineTotal { get; set; }
}

public class Refund
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;

    public int? ProcessedById { get; set; }
    public User? ProcessedBy { get; set; }
    public string ProcessedName { get; set; } = string.Empty;   

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<RefundItem> Items { get; set; } = new List<RefundItem>();
}

public class RefundItem
{
    public int Id { get; set; }
    public int RefundId { get; set; }
    public Refund Refund { get; set; } = null!;
    public int? SaleItemId { get; set; }
    public SaleItem? SaleItem { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}