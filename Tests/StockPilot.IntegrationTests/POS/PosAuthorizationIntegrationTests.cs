
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

public partial class PosIntegrationTests
{
    [Fact]
    public async Task Checkout_WithNonexistentStore_ReturnsNotFound()
    {
        var fixture = await CreateCheckoutFixtureAsync();
        var ownerToken = await CreateRoleTokenAsync(null, UserRole.Owner);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ownerToken);

        var request = new CheckoutRequest(
            int.MaxValue,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 250m);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WithInactiveStore_ReturnsNotFound()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var store = await db.Stores.SingleAsync(
                s => s.Id == fixture.StoreId);

            store.IsActive = false;
            await db.SaveChangesAsync();
        }

        var ownerToken = await CreateRoleTokenAsync(null, UserRole.Owner);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ownerToken);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 250m);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WithoutAuthentication_ReturnsUnauthorized()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = _factory.CreateClient();

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 250m);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WithInventoryClerkRole_ReturnsForbidden()
    {
        var fixture = await CreateCheckoutFixtureAsync();
        var token = await CreateRoleTokenAsync(
            fixture.StoreId, UserRole.InventoryClerk);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 250m);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WithManagerRole_IsAllowed()
    {
        var fixture = await CreateCheckoutFixtureAsync();
        var token = await CreateRoleTokenAsync(
            fixture.StoreId, UserRole.Manager);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 112m);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(112m, result.Receipt.Total);
    }

    [Fact]
    public async Task Checkout_WithOwnerRoleAndStoreId_IsAllowed()
    {
        var fixture = await CreateCheckoutFixtureAsync();
        var token = await CreateRoleTokenAsync(
            null, UserRole.Owner);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 112m);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(112m, result.Receipt.Total);
    }

    [Fact]
    public async Task Checkout_WithOwnerRoleWithoutStoreId_ReturnsBadRequest()
    {
        var fixture = await CreateCheckoutFixtureAsync();
        var token = await CreateRoleTokenAsync(
            null, UserRole.Owner);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new CheckoutRequest(
            null,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 250m);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WithCashierWithoutStoreClaim_ReturnsForbidden()
    {
        var fixture = await CreateCheckoutFixtureAsync();
        var token = await CreateRoleTokenAsync(
            null, UserRole.Cashier);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 250m);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WhenCashierRequestsAnotherStore_UsesAssignedStoreOnly()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        int otherStoreId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var otherStore = new Store
            {
                Code = $"T{Guid.NewGuid():N}"[..8],
                Name = "Another Test Store",
                Currency = "PHP",
                TaxRate = 12m,
                TimeZone = "Asia/Manila",
                IsActive = true
            };

            db.Stores.Add(otherStore);
            await db.SaveChangesAsync();

            otherStoreId = otherStore.Id;

            db.InventoryLevels.Add(new InventoryLevel
            {
                StoreId = otherStoreId,
                ProductId = fixture.ProductId,
                QuantityOnHand = 30
            });

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        // The cashier attempts to specify another store.
        var request = new CheckoutRequest(
            otherStoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 1) },
            0m, "Cash", 112m);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb =
            verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await verificationDb.Sales.SingleAsync(
            s => s.Id == result.Receipt.SaleId);

        // The transaction must use the cashier's assigned store.
        Assert.Equal(fixture.StoreId, sale.StoreId);
        Assert.NotEqual(otherStoreId, sale.StoreId);

        var assignedStoreInventory =
            await verificationDb.InventoryLevels.SingleAsync(
                i => i.StoreId == fixture.StoreId &&
                     i.ProductId == fixture.ProductId);

        var otherStoreInventory =
            await verificationDb.InventoryLevels.SingleAsync(
                i => i.StoreId == otherStoreId &&
                     i.ProductId == fixture.ProductId);

        Assert.Equal(19, assignedStoreInventory.QuantityOnHand);
        Assert.Equal(30, otherStoreInventory.QuantityOnHand);
    }

    private async Task<string> CreateRoleTokenAsync(
        int? storeId,
        UserRole role)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Email = $"{role}-{Guid.NewGuid():N}@stockpilot.test",
            PasswordHash = "integration-test-password-hash",
            FirstName = "Test",
            Surname = role.ToString(),
            Role = role,
            IsActive = true,
            IsFirstLogin = false,
            StoreId = storeId
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var tokenService =
            scope.ServiceProvider.GetRequiredService<ITokenService>();

        var (token, _) = tokenService.CreateToken(user);

        return token;
    }
}