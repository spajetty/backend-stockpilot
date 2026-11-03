namespace backend_stockpilot.Dtos.Stores;

public record StoreDto(
    int Id,
    string Code,
    string Name,
    string Currency,
    decimal TaxRate,
    string TimeZone);