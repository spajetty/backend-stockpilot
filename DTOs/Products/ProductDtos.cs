namespace backend_stockpilot.Dtos.Products;

public record LookupOptionDto(int Id, string Name);

public record CreateLookupRequest(string Name);

public record ProductListItemDto(
    int Id,
    string Name,
    string Sku,
    string? Barcode,
    int? CategoryId,
    string? CategoryName,
    int? SupplierId,
    string? SupplierName,
    decimal CostPrice,
    decimal SellingPrice,
    decimal Margin,
    int Stock,
    int ReorderLevel,
    string? ImageUrl,
    string Status,                  // Active | Inactive | Pending | Rejected
    bool AvailableInPos,
    string? RequestedByStoreName,   // only set while a manager's product is pending/rejected
    int PendingOpeningStock);

public record ProductsPageDto(
    List<ProductListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public record ProductMovementDto(
    long Id,
    string Type,
    string? Reference,
    string? Reason,
    int QuantityChange,
    string StoreCode,
    DateTime CreatedAt);

public record ProductSaleDto(
    int SaleId,
    string SaleNumber,
    DateTime CreatedAt,
    string CashierName,
    decimal Total);

public record ProductDetailDto(
    int Id,
    string Name,
    string Sku,
    string? Barcode,
    int? CategoryId,
    string? CategoryName,
    int? SupplierId,
    string? SupplierName,
    decimal CostPrice,
    decimal SellingPrice,
    decimal Margin,
    int Stock,
    int ReorderLevel,
    string? ImageUrl,
    string Status,
    bool AvailableInPos,
    string? RequestedByStoreName,
    int PendingOpeningStock,
    string? Description,
    decimal InventoryValue,
    string? RejectionReason,
    List<ProductMovementDto> RecentMovements,
    List<ProductSaleDto> RecentSales);

public record ProductCreateRequest(
    string Name,
    string? Barcode,
    int? CategoryId,
    int? SupplierId,
    decimal CostPrice,
    decimal SellingPrice,
    int ReorderLevel,
    string? ImageUrl,
    string? Description,
    bool AvailableInPos,
    int OpeningStock,                       
    List<StoreStockRequest>? StoreStocks); 

public record StoreStockRequest(int StoreId, int Quantity);

public record ProductUpdateRequest(
    string Name,
    string? Barcode,
    int? CategoryId,
    int? SupplierId,
    decimal CostPrice,
    decimal SellingPrice,
    int ReorderLevel,
    string? ImageUrl,
    string? Description,
    bool AvailableInPos,
    int? PendingOpeningStock);      // only used while the product is pending or rejected

public record RejectProductRequest(string Reason);

public record DuplicateCheckDto(Dictionary<string, string> Fields);