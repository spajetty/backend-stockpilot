namespace backend_stockpilot.Models;

public enum PaymentGatewayStatus
{
    Created,
    Approved,
    Captured,
    Failed,
    Cancelled,
    Refunded
}

// One row per gateway attempt. A sale can have several (a failed PayPal order, then cash for example lang).
public class Payment
{
    public int Id { get; set; }

    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }

    public PaymentMethod Provider { get; set; }
    public string? ProviderOrderId { get; set; }      // PayPal order id
    public string? ProviderCaptureId { get; set; }
    public PaymentGatewayStatus Status { get; set; } = PaymentGatewayStatus.Created;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    public string? FailureReason { get; set; }

    public string? RawPayload { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}