using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Pos;
using backend_stockpilot.Models;
using backend_stockpilot.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StockPilot.IntegrationTests.POS;

[Collection("Integration Tests")]
public partial class PosIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;

    public PosIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Checkout_WithValidCashPayment_CreatesSaleAndDeductsInventory()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
                new(fixture.ProductId, 2)
            },
            0m,
            "Cash",
            250m
        );

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        // Assert: HTTP response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        // Assert: Receipt calculations
        Assert.Equal(200m, result.Receipt.Subtotal);
        Assert.Equal(0m, result.Receipt.Discount);
        Assert.Equal(24m, result.Receipt.Tax);
        Assert.Equal(224m, result.Receipt.Total);
        Assert.Equal(250m, result.Receipt.CashTendered);
        Assert.Equal(26m, result.Receipt.ChangeDue);
        Assert.Equal("Cash", result.Receipt.PaymentMethod);

        Assert.Single(result.Receipt.Items);
        Assert.Equal(2, result.Receipt.Items[0].Quantity);
        Assert.Equal(100m, result.Receipt.Items[0].UnitPrice);
        Assert.Equal(200m, result.Receipt.Items[0].LineTotal);

        // Assert: Stock change response
        Assert.Single(result.StockChanges);
        Assert.Equal(fixture.ProductId, result.StockChanges[0].ProductId);
        Assert.Equal(18, result.StockChanges[0].Quantity);

        // Assert: Database persistence
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await db.Sales
            .Include(s => s.Items)
            .SingleAsync(s => s.Id == result.Receipt.SaleId);

        Assert.Equal(200m, sale.Subtotal);
        Assert.Equal(0m, sale.Discount);
        Assert.Equal(24m, sale.Tax);
        Assert.Equal(224m, sale.Total);
        Assert.Equal(PaymentMethod.Cash, sale.PaymentMethod);
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(SalePaymentStatus.Paid, sale.PaymentStatus);

        Assert.Single(sale.Items);

        var saleItem = sale.Items.Single();

        Assert.Equal(fixture.ProductId, saleItem.ProductId);
        Assert.Equal(2, saleItem.Quantity);
        Assert.Equal(100m, saleItem.UnitPrice);
        Assert.Equal(60m, saleItem.UnitCost);
        Assert.Equal(200m, saleItem.LineTotal);

        // Assert: Inventory deduction
        var inventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(18, inventory.QuantityOnHand);

        // Assert: Stock movement
        var movement = await db.StockMovements.SingleAsync(
            m => m.Reference == sale.SaleNumber);

        Assert.Equal(MovementType.Sale, movement.MovementType);
        Assert.Equal(-2, movement.QuantityChange);
        Assert.Equal(20, movement.QuantityBefore);
        Assert.Equal(18, movement.QuantityAfter);

        // Assert: Audit log
        var audit = await db.AuditLogs.SingleAsync(
            a => a.EntityId == sale.SaleNumber);

        Assert.Equal("Sale completed", audit.Action);
        Assert.Equal("Sale", audit.Entity);
        Assert.Equal(AuditOutcome.Success, audit.Outcome);

        // Cash payments should not create a separate Payment record.
        Assert.False(await db.Payments.AnyAsync(
            p => p.SaleId == sale.Id));
    }

    private async Task<(int StoreId, int ProductId, string Token)>
        CreateCheckoutFixtureAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var storeCode = $"T{Guid.NewGuid():N}"[..8];

        var store = new Store
        {
            Code = storeCode,
            Name = "POS Integration Test Store",
            Currency = "PHP",
            TaxRate = 12m,
            TimeZone = "Asia/Manila",
            IsActive = true
        };

        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var user = new User
        {
            Email = $"cashier-{Guid.NewGuid():N}@stockpilot.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("testpassword"),
            FirstName = "Test",
            Surname = "Cashier",
            Role = UserRole.Cashier,
            IsActive = true,
            IsFirstLogin = false,
            StoreId = store.Id
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var product = new Product
        {
            Name = "Integration Test Product",
            Sku = $"SKU-{Guid.NewGuid():N}",
            CostPrice = 60m,
            SellingPrice = 100m,
            ReorderLevel = 5,
            IsActive = true
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();

        db.InventoryLevels.Add(new InventoryLevel
        {
            StoreId = store.Id,
            ProductId = product.Id,
            QuantityOnHand = 20
        });

        await db.SaveChangesAsync();

        var tokenService =
            scope.ServiceProvider.GetRequiredService<ITokenService>();

        var (token, _) = tokenService.CreateToken(user);

        return (store.Id, product.Id, token);
    }

    [Fact]
    public async Task Checkout_WithValidCardPayment_CreatesPaymentRecordAndDeductsInventory()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
            new(fixture.ProductId, 2)
            },
            0m,
            "Card",
            null
        );

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        // Assert: HTTP response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        // Assert: Receipt calculations
        Assert.Equal(200m, result.Receipt.Subtotal);
        Assert.Equal(24m, result.Receipt.Tax);
        Assert.Equal(224m, result.Receipt.Total);
        Assert.Equal("Card", result.Receipt.PaymentMethod);

        // Card payments should not have cash tendered or change.
        Assert.Null(result.Receipt.CashTendered);
        Assert.Null(result.Receipt.ChangeDue);

        // Assert: Database persistence
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await db.Sales
            .SingleAsync(s => s.Id == result.Receipt.SaleId);

        Assert.Equal(PaymentMethod.Card, sale.PaymentMethod);
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(SalePaymentStatus.Paid, sale.PaymentStatus);
        Assert.Equal(224m, sale.Total);

        // Assert: Payment record
        var payment = await db.Payments
            .SingleAsync(p => p.SaleId == sale.Id);

        Assert.Equal(fixture.StoreId, payment.StoreId);
        Assert.Equal(PaymentMethod.Card, payment.Provider);
        Assert.Equal(PaymentGatewayStatus.Captured, payment.Status);
        Assert.Equal(224m, payment.Amount);
        Assert.Equal("PHP", payment.Currency);

        // Assert: Inventory deduction
        var inventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(18, inventory.QuantityOnHand);

        // Assert: Stock movement
        var movement = await db.StockMovements.SingleAsync(
            m => m.Reference == sale.SaleNumber);

        Assert.Equal(-2, movement.QuantityChange);
        Assert.Equal(20, movement.QuantityBefore);
        Assert.Equal(18, movement.QuantityAfter);
    }


    [Fact]
    public async Task Checkout_WithValidPayPalPayment_CreatesPaymentRecord()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            0m,
            "PayPal",
            null
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/pos/checkout", request);
        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(224m, result.Receipt.Total);
        Assert.Equal("PayPal", result.Receipt.PaymentMethod);
        Assert.Null(result.Receipt.CashTendered);
        Assert.Null(result.Receipt.ChangeDue);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await db.Sales.SingleAsync(s => s.Id == result.Receipt.SaleId);
        var payment = await db.Payments.SingleAsync(p => p.SaleId == sale.Id);

        Assert.Equal(PaymentMethod.PayPal, sale.PaymentMethod);
        Assert.Equal(PaymentMethod.PayPal, payment.Provider);
        Assert.Equal(PaymentGatewayStatus.Captured, payment.Status);
        Assert.Equal(224m, payment.Amount);
        Assert.Equal("PHP", payment.Currency);
    }

    [Fact]
    public async Task Checkout_WithMonetaryDiscount_CalculatesCorrectTotal()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            20m,
            "Cash",
            210m
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/pos/checkout", request);
        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        Assert.Equal(200m, result.Receipt.Subtotal);
        Assert.Equal(20m, result.Receipt.Discount);
        Assert.Equal(21.60m, result.Receipt.Tax);
        Assert.Equal(201.60m, result.Receipt.Total);
        Assert.Equal(210m, result.Receipt.CashTendered);
        Assert.Equal(8.40m, result.Receipt.ChangeDue);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await db.Sales.SingleAsync(s => s.Id == result.Receipt.SaleId);

        Assert.Equal(20m, sale.Discount);
        Assert.Equal(21.60m, sale.Tax);
        Assert.Equal(201.60m, sale.Total);
    }

    [Fact]
    public async Task Checkout_WithExactCashTendered_ReturnsZeroChange()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            0m,
            "Cash",
            224m
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/pos/checkout", request);
        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        Assert.Equal(224m, result.Receipt.Total);
        Assert.Equal(224m, result.Receipt.CashTendered);
        Assert.Equal(0m, result.Receipt.ChangeDue);
    }

    [Fact]
    public async Task Checkout_WithMultipleProducts_CreatesMultipleSaleItems()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        int secondProductId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var secondProduct = new Product
            {
                Name = "Second Integration Test Product",
                Sku = $"SKU-{Guid.NewGuid():N}",
                CostPrice = 30m,
                SellingPrice = 50m,
                ReorderLevel = 5,
                IsActive = true
            };

            db.Products.Add(secondProduct);
            await db.SaveChangesAsync();

            db.InventoryLevels.Add(new InventoryLevel
            {
                StoreId = fixture.StoreId,
                ProductId = secondProduct.Id,
                QuantityOnHand = 10
            });

            await db.SaveChangesAsync();
            secondProductId = secondProduct.Id;
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
            new(fixture.ProductId, 2),
            new(secondProductId, 1)
            },
            0m,
            "Cash",
            300m
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/pos/checkout", request);
        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        Assert.Equal(250m, result.Receipt.Subtotal);
        Assert.Equal(30m, result.Receipt.Tax);
        Assert.Equal(280m, result.Receipt.Total);
        Assert.Equal(20m, result.Receipt.ChangeDue);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb =
            verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await verificationDb.Sales
            .Include(s => s.Items)
            .SingleAsync(s => s.Id == result.Receipt.SaleId);

        Assert.Equal(2, sale.Items.Count);
        Assert.Contains(sale.Items, i => i.ProductId == fixture.ProductId);
        Assert.Contains(sale.Items, i => i.ProductId == secondProductId);

        var firstInventory = await verificationDb.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId && i.ProductId == fixture.ProductId);

        var secondInventory = await verificationDb.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId && i.ProductId == secondProductId);

        Assert.Equal(18, firstInventory.QuantityOnHand);
        Assert.Equal(9, secondInventory.QuantityOnHand);
    }

    [Fact]
    public async Task Checkout_WithDuplicateProductLines_CombinesQuantities()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
            new(fixture.ProductId, 1),
            new(fixture.ProductId, 2)
            },
            0m,
            "Cash",
            350m
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/pos/checkout", request);
        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        Assert.Equal(300m, result.Receipt.Subtotal);
        Assert.Equal(36m, result.Receipt.Tax);
        Assert.Equal(336m, result.Receipt.Total);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await db.Sales
            .Include(s => s.Items)
            .SingleAsync(s => s.Id == result.Receipt.SaleId);

        // Duplicate product lines should become one sale item.
        Assert.Single(sale.Items);
        Assert.Equal(3, sale.Items.Single().Quantity);
        Assert.Equal(300m, sale.Items.Single().LineTotal);

        var inventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(17, inventory.QuantityOnHand);

        var movement = await db.StockMovements.SingleAsync(
            m => m.Reference == sale.SaleNumber);

        Assert.Equal(-3, movement.QuantityChange);
    }

    [Fact]
    public async Task Checkout_WithFractionalPrice_CalculatesRoundedTaxAndTotal()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var product = await db.Products.SingleAsync(
                p => p.Id == fixture.ProductId);

            product.SellingPrice = 10.05m;
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m,
            "Cash",
            20m
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/pos/checkout", request);
        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        Assert.Equal(10.05m, result.Receipt.Subtotal);
        Assert.Equal(1.21m, result.Receipt.Tax);
        Assert.Equal(11.26m, result.Receipt.Total);
        Assert.Equal(8.74m, result.Receipt.ChangeDue);
    }

    [Fact]
    public async Task Checkout_WithZeroTaxStore_DoesNotAddTax()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var store = await db.Stores.SingleAsync(
                s => s.Id == fixture.StoreId);

            store.TaxRate = 0m;
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            0m,
            "Cash",
            200m
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/pos/checkout", request);
        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        Assert.Equal(200m, result.Receipt.Subtotal);
        Assert.Equal(0m, result.Receipt.Tax);
        Assert.Equal(200m, result.Receipt.Total);
        Assert.Equal(0m, result.Receipt.ChangeDue);
    }
}