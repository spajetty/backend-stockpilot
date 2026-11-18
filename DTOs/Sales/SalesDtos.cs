namespace backend_stockpilot.Dtos.Sales;

public record SaleListItemDto(
    int Id,
    string SaleNumber,
    DateTime CreatedAt,
    string CashierName,
    int Items,
    decimal Subtotal,
    decimal Total,
    string PaymentMethod,
    string PaymentStatus,
    string Status);

public record SalesPageDto(
    List<SaleListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public record SaleDetailItemDto(
    int Id,
    string Name,
    string Sku,
    int Quantity,
    int RefundedQuantity,
    decimal UnitPrice,
    decimal LineTotal);

public record SaleRefundDto(int Id, decimal Amount, string Reason, string ProcessedBy, DateTime CreatedAt);

public record SaleDetailDto(
    int Id,
    string SaleNumber,
    DateTime CreatedAt,
    string CashierName,
    string PaymentMethod,
    string PaymentStatus,
    string Status,
    decimal Subtotal,
    decimal Discount,
    decimal Tax,
    decimal Total,
    decimal RefundedTotal,
    List<SaleDetailItemDto> Items,
    List<SaleRefundDto> Refunds);

public record RefundItemRequest(int SaleItemId, int Quantity);

public record RefundRequest(List<RefundItemRequest> Items, string Reason, bool ReturnToStock);