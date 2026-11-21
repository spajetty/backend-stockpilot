using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Sales;
using backend_stockpilot.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace StockPilot.IntegrationTests;

[Collection("Integration Tests")]
public partial class SalesIntegrationTests
{
    private const string StoreContextHeader = "X-Store-Id";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly CustomWebApplicationFactory _factory;

    public SalesIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record TestUser(int Id, string Email, string Name, UserRole Role, int? StoreId);

    private sealed record Line(Product? Product, int Quantity, decimal UnitPrice, decimal? LineTotal = null);

    private sealed record Scenario(Store Store, TestUser Manager, TestUser Cashier, TestUser Owner);

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_factory.DbConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static string Uid(int length = 8) =>
        Guid.NewGuid().ToString("N")[..length].ToUpperInvariant();

    private async Task<Store> CreateStoreAsync()
    {
        await using var db = NewDb();
        var store = new Store
        {
            Code = "T" + Uid(6),
            Name = "Test Store " + Uid(6),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store;
    }

    private async Task<TestUser> CreateUserAsync(UserRole role, int? storeId)
    {
        var uid = Uid();
        await using var db = NewDb();
        var user = new User
        {
            Email = $"{role}.{uid}@sales-tests.local".ToLowerInvariant(),
            PasswordHash = "not-a-real-hash",
            FirstName = role.ToString(),
            Surname = uid,
            Role = role,
            IsActive = true,
            IsFirstLogin = false,
            StoreId = storeId,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new TestUser(user.Id, user.Email, $"{role} {uid}", role, storeId);
    }

    private async Task<Scenario> NewScenarioAsync()
    {
        var store = await CreateStoreAsync();
        var manager = await CreateUserAsync(UserRole.Manager, store.Id);
        var cashier = await CreateUserAsync(UserRole.Cashier, store.Id);
        var owner = await CreateUserAsync(UserRole.Owner, null);
        return new Scenario(store, manager, cashier, owner);
    }

    private async Task<Product> CreateProductAsync(string? name = null, decimal price = 100m, decimal cost = 60m)
    {
        await using var db = NewDb();
        var product = new Product
        {
            Name = name ?? "Test Product " + Uid(),
            Sku = "SKU-" + Uid(),
            SellingPrice = price,
            CostPrice = cost,
            ReorderLevel = 5,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private async Task<Sale> CreateSaleAsync(
        Store store,
        TestUser? cashier,
        IReadOnlyList<Line> lines,
        decimal discount = 0m,
        PaymentMethod method = PaymentMethod.Cash,
        SaleStatus status = SaleStatus.Completed,
        SalePaymentStatus payment = SalePaymentStatus.Paid,
        DateTime? createdAt = null)
    {
        var sale = new Sale
        {
            StoreId = store.Id,
            SaleNumber = "SP-TEST-" + Uid(),
            CashierId = cashier?.Id,
            CashierName = cashier?.Name ?? "Test Cashier",
            PaymentMethod = method,
            PaymentStatus = payment,
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        foreach (var l in lines)
        {
            sale.Items.Add(new SaleItem
            {
                ProductId = l.Product?.Id,
                ProductName = l.Product?.Name ?? "Discontinued product",
                Sku = l.Product?.Sku ?? "SKU-GONE-" + Uid(),
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                UnitCost = l.Product?.CostPrice ?? 0m,
                LineTotal = l.LineTotal ?? l.UnitPrice * l.Quantity,
            });
        }

        var subtotal = sale.Items.Sum(i => i.LineTotal);
        var tax = Math.Round((subtotal - discount) * 0.12m, 2, MidpointRounding.AwayFromZero);
        sale.Subtotal = subtotal;
        sale.Discount = discount;
        sale.Tax = tax;
        sale.Total = subtotal - discount + tax;

        await using var db = NewDb();
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        return sale;
    }

    private async Task CreateSalesBulkAsync(Store store, TestUser cashier, int count)
    {
        await using var db = NewDb();
        for (var i = 0; i < count; i++)
        {
            var sale = new Sale
            {
                StoreId = store.Id,
                SaleNumber = "SP-TEST-" + Uid(),
                CashierId = cashier.Id,
                CashierName = cashier.Name,
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = SalePaymentStatus.Paid,
                Status = SaleStatus.Completed,
                Subtotal = 100m,
                Discount = 0m,
                Tax = 12m,
                Total = 112m,
                CreatedAt = DateTime.UtcNow.AddSeconds(-i),
            };
            sale.Items.Add(new SaleItem
            {
                ProductName = "Bulk item",
                Sku = "SKU-BULK-" + Uid(),
                Quantity = 1,
                UnitPrice = 100m,
                UnitCost = 0m,
                LineTotal = 100m,
            });
            db.Sales.Add(sale);
        }
        await db.SaveChangesAsync();
    }

    private async Task SetStockAsync(int storeId, int productId, int quantity)
    {
        await using var db = NewDb();
        db.InventoryLevels.Add(new InventoryLevel
        {
            StoreId = storeId,
            ProductId = productId,
            QuantityOnHand = quantity,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<int?> GetStockAsync(int storeId, int productId)
    {
        await using var db = NewDb();
        var level = await db.InventoryLevels.AsNoTracking()
            .FirstOrDefaultAsync(i => i.StoreId == storeId && i.ProductId == productId);
        return level?.QuantityOnHand;
    }

    private async Task<Sale> ReloadSaleAsync(int saleId)
    {
        await using var db = NewDb();
        return await db.Sales.AsNoTracking()
            .Include(s => s.Items)
            .Include(s => s.Refunds).ThenInclude(r => r.Items)
            .SingleAsync(s => s.Id == saleId);
    }

    private static string Token(TestUser user)
    {
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email),
            new("name", user.Name),
            new("role", user.Role.ToString()),
        };
        if (user.StoreId is int storeId) claims.Add(new Claim("store_id", storeId.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.JwtKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = CustomWebApplicationFactory.JwtIssuer,
            Audience = CustomWebApplicationFactory.JwtAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private HttpClient ClientFor(
        TestUser user,
        int? storeContext = null,
        WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? _factory).CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token(user));
        if (storeContext is int storeId)
            client.DefaultRequestHeaders.Add(StoreContextHeader, storeId.ToString());
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(Json);
        return value ?? throw new InvalidOperationException("Empty response body.");
    }

    private static async Task<string> ReadMessageAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("message").GetString() ?? "";
    }

    private static async Task<SalesPageDto> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/sales" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<SalesPageDto>(response);
    }

    private static async Task<SaleDetailDto> GetDetailAsync(HttpClient client, int saleId)
    {
        var response = await client.GetAsync($"/api/sales/{saleId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<SaleDetailDto>(response);
    }

    private static RefundRequest Req(string? reason, bool returnToStock, params (int SaleItemId, int Quantity)[] items) =>
        new(items.Select(i => new RefundItemRequest(i.SaleItemId, i.Quantity)).ToList(), reason!, returnToStock);

    private static Task<HttpResponseMessage> PostRefundAsync(HttpClient client, int saleId, RefundRequest request) =>
        client.PostAsJsonAsync($"/api/sales/{saleId}/refunds", request, Json);

    private static int ItemId(Sale sale, int index) =>
        sale.Items.OrderBy(i => i.Id).ElementAt(index).Id;
}
