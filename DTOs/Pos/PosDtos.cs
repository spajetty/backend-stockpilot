namespace backend_stockpilot.Dtos.Pos;

public record PosCategoryDto(int Id, string Name);

public record PosProductDto(
    int Id,
    string Name,
    string Sku,
    string? Barcode,
    int? CategoryId,
    decimal Price,
    string? ImageUrl,
    int Stock,
    int ReorderLevel);       

public record PosCatalogDto(
    int StoreId,
    decimal TaxRate,
    List<PosCategoryDto> Categories,
    List<PosProductDto> Products);

public record StockChange(int ProductId, int Quantity);

public record CheckoutItemDto(int ProductId, int Quantity);

public record CheckoutRequest(
    int? StoreId,                 
    List<CheckoutItemDto> Items,
    decimal Discount,
    string PaymentMethod,    
    decimal? CashTendered);

public record ReceiptItemDto(string Name, string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);

public record ReceiptDto(
    int SaleId, string SaleNumber, DateTime CreatedAt, string StoreName, string CashierName,
    List<ReceiptItemDto> Items, decimal Subtotal, decimal Discount, decimal TaxRate,
    decimal Tax, decimal Total, string PaymentMethod, decimal? CashTendered, decimal? ChangeDue);

public record CheckoutResponse(ReceiptDto Receipt, List<StockChange> StockChanges);