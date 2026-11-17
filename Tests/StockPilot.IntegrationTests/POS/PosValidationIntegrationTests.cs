using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Pos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StockPilot.IntegrationTests.POS;

public partial class PosIntegrationTests
{
    [Fact]
    public async Task Checkout_WithEmptyCart_ReturnsBadRequest()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>(),
            0m,
            "Cash",
            250m
        );

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Sales.AnyAsync(
            s => s.StoreId == fixture.StoreId));
    }

    [Fact]
    public async Task Checkout_WithNullItems_ReturnsBadRequest()
    {
        // Arrange
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            null!,
            0m,
            "Cash",
            250m
        );

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Sales.AnyAsync(
            s => s.StoreId == fixture.StoreId));
    }

    [Fact]
    public async Task Checkout_WithZeroQuantity_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 0) },
            0m, "Cash", 250m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithNegativeQuantity_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, -1) },
            0m, "Cash", 250m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithQuantityExceeding999_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1000) },
            0m, "Cash", 250m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithInvalidPaymentMethod_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Bitcoin", 250m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithNegativeDiscount_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            -1m, "Cash", 250m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithDiscountExceedingSubtotal_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            201m, "Cash", 250m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithMissingCashTendered_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            0m, "Cash", null);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithInsufficientCashTendered_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            0m, "Cash", 223.99m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithNonexistentProduct_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(int.MaxValue, 1) },
            0m, "Cash", 250m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithInactiveProduct_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var product = await db.Products.SingleAsync(
                p => p.Id == fixture.ProductId);

            product.IsActive = false;
            await db.SaveChangesAsync();
        }

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 250m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }

    [Fact]
    public async Task Checkout_WithArchivedProduct_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var product = await db.Products.SingleAsync(
                p => p.Id == fixture.ProductId);

            product.ArchivedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 250m);

        await AssertRejectedCheckoutAsync(fixture, request);
    }


    private async Task AssertRejectedCheckoutAsync(
        (int StoreId, int ProductId, string Token) fixture,
        CheckoutRequest request)
    {
        using var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // No sale should be created.
        Assert.False(await db.Sales.AnyAsync(
            s => s.StoreId == fixture.StoreId));

        // No payment should be created.
        Assert.False(await db.Payments.AnyAsync(
            p => p.StoreId == fixture.StoreId));

        // Inventory should remain unchanged.
        var inventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(20, inventory.QuantityOnHand);

        // No stock movement should be recorded.
        Assert.False(await db.StockMovements.AnyAsync(
            m => m.StoreId == fixture.StoreId));

        // No success audit log should be created.
        Assert.False(await db.AuditLogs.AnyAsync(
            a => a.StoreId == fixture.StoreId &&
                 a.Action == "Sale completed"));
    }
}