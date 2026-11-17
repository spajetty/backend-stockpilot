using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Pos;
using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StockPilot.IntegrationTests.POS;

public partial class PosIntegrationTests
{
    [Fact]
    public async Task Checkout_WithInsufficientStock_ReturnsConflict()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await SetInventoryQuantityAsync(fixture, 1);

        var request = CreateCashRequest(fixture, 2);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await AssertFailedCheckoutHasNoSideEffectsAsync(fixture, 1);
    }

    [Fact]
    public async Task Checkout_WithMissingInventoryRecord_ReturnsConflict()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var inventory = await db.InventoryLevels.SingleAsync(
                i => i.StoreId == fixture.StoreId &&
                     i.ProductId == fixture.ProductId);

            db.InventoryLevels.Remove(inventory);
            await db.SaveChangesAsync();
        }

        var request = CreateCashRequest(fixture, 1);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var verificationScope =
            _factory.Services.CreateAsyncScope();

        var verificationDb =
            verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await verificationDb.InventoryLevels.AnyAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId));

        Assert.False(await verificationDb.Sales.AnyAsync(
            s => s.StoreId == fixture.StoreId));

        Assert.False(await verificationDb.StockMovements.AnyAsync(
            m => m.StoreId == fixture.StoreId));
    }

    [Fact]
    public async Task Checkout_WithMultipleProductsAndInsufficientStock_RollsBackAllInventoryChanges()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var secondProductId =
            await CreateAdditionalProductWithInventoryAsync(
                fixture.StoreId, 1);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
                new(fixture.ProductId, 1),
                new(secondProductId, 2)
            },
            0m,
            "Cash",
            500m);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The first product must not remain deducted.
        var firstInventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(20, firstInventory.QuantityOnHand);

        // The second product must also remain unchanged.
        var secondInventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == secondProductId);

        Assert.Equal(1, secondInventory.QuantityOnHand);

        // No partial transaction records should survive.
        Assert.False(await db.Sales.AnyAsync(
            s => s.StoreId == fixture.StoreId));

        Assert.False(await db.Payments.AnyAsync(
            p => p.StoreId == fixture.StoreId));

        Assert.False(await db.StockMovements.AnyAsync(
            m => m.StoreId == fixture.StoreId));

        Assert.False(await db.AuditLogs.AnyAsync(
            a => a.StoreId == fixture.StoreId &&
                 a.Action == "Sale completed"));
    }

    [Fact]
    public async Task Checkout_WhenStockIsInsufficient_CreatesNoSale()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await SetInventoryQuantityAsync(fixture, 1);

        var request = CreateCashRequest(fixture, 2);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Sales.AnyAsync(
            s => s.StoreId == fixture.StoreId));
    }

    [Fact]
    public async Task Checkout_WhenStockIsInsufficient_CreatesNoPayment()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await SetInventoryQuantityAsync(fixture, 1);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
                new(fixture.ProductId, 2)
            },
            0m,
            "Card",
            null);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Payments.AnyAsync(
            p => p.StoreId == fixture.StoreId));
    }

    [Fact]
    public async Task Checkout_WhenStockIsInsufficient_CreatesNoSuccessAuditLog()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await SetInventoryQuantityAsync(fixture, 1);

        var request = CreateCashRequest(fixture, 2);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.AuditLogs.AnyAsync(
            a => a.StoreId == fixture.StoreId &&
                 a.Action == "Sale completed"));
    }

    [Fact]
    public async Task Checkout_WithInsufficientStock_NeverMakesInventoryNegative()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await SetInventoryQuantityAsync(fixture, 20);

        var request = CreateCashRequest(fixture, 21);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(20, inventory.QuantityOnHand);
        Assert.True(inventory.QuantityOnHand >= 0);
    }

    [Fact]
    public async Task Checkout_CreatesCorrectStockMovement()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = CreateCashRequest(fixture, 2);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var movement = await db.StockMovements.SingleAsync(
            m => m.StoreId == fixture.StoreId &&
                 m.ProductId == fixture.ProductId);

        Assert.Equal(MovementType.Sale, movement.MovementType);
        Assert.Equal(-2, movement.QuantityChange);
        Assert.Equal(20, movement.QuantityBefore);
        Assert.Equal(18, movement.QuantityAfter);
    }

    [Fact]
    public async Task Checkout_PersistsCorrectSaleItems()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = CreateCashRequest(fixture, 2);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.NotNull(result);

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await db.Sales
            .Include(s => s.Items)
            .SingleAsync(s => s.Id == result.Receipt.SaleId);

        var item = Assert.Single(sale.Items);

        Assert.Equal(fixture.ProductId, item.ProductId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal("Integration Test Product", item.ProductName);
        Assert.Equal(100m, item.UnitPrice);
        Assert.Equal(60m, item.UnitCost);
        Assert.Equal(200m, item.LineTotal);
    }

    [Fact]
    public async Task Checkout_WithCardPayment_MatchesSaleAndPaymentAmounts()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
                new(fixture.ProductId, 2)
            },
            0m,
            "Card",
            null);

        using var client = CreateAuthenticatedClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.NotNull(result);

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await db.Sales.SingleAsync(
            s => s.Id == result.Receipt.SaleId);

        var payment = await db.Payments.SingleAsync(
            p => p.SaleId == sale.Id);

        // Subtotal: 200
        // Tax: 24
        // Total: 224
        Assert.Equal(200m, sale.Subtotal);
        Assert.Equal(24m, sale.Tax);
        Assert.Equal(224m, sale.Total);

        Assert.Equal(sale.Total, payment.Amount);
        Assert.Equal(sale.Id, payment.SaleId);
        Assert.Equal(sale.StoreId, payment.StoreId);

        Assert.Equal(PaymentMethod.Card, payment.Provider);
        Assert.Equal(PaymentGatewayStatus.Captured, payment.Status);
    }

    private static CheckoutRequest CreateCashRequest(
        (int StoreId, int ProductId, string Token) fixture,
        int quantity)
    {
        return new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
                new(fixture.ProductId, quantity)
            },
            0m,
            "Cash",
            5000m);
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private async Task SetInventoryQuantityAsync(
        (int StoreId, int ProductId, string Token) fixture,
        int quantity)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        inventory.QuantityOnHand = quantity;

        await db.SaveChangesAsync();
    }

    private async Task<int> CreateAdditionalProductWithInventoryAsync(
        int storeId,
        int initialStock)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product
        {
            Name = "Second Test Product",
            Sku = $"TEST-{Guid.NewGuid():N}",
            CostPrice = 30m,
            SellingPrice = 50m,
            ReorderLevel = 5,
            IsActive = true
        };

        db.Products.Add(product);

        await db.SaveChangesAsync();

        db.InventoryLevels.Add(new InventoryLevel
        {
            StoreId = storeId,
            ProductId = product.Id,
            QuantityOnHand = initialStock
        });

        await db.SaveChangesAsync();

        return product.Id;
    }

    private async Task AssertFailedCheckoutHasNoSideEffectsAsync(
        (int StoreId, int ProductId, string Token) fixture,
        int expectedInventory)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Sales.AnyAsync(
            s => s.StoreId == fixture.StoreId));

        Assert.False(await db.Payments.AnyAsync(
            p => p.StoreId == fixture.StoreId));

        var inventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(expectedInventory, inventory.QuantityOnHand);

        Assert.False(await db.StockMovements.AnyAsync(
            m => m.StoreId == fixture.StoreId));

        Assert.False(await db.AuditLogs.AnyAsync(
            a => a.StoreId == fixture.StoreId &&
                 a.Action == "Sale completed"));
    }
}