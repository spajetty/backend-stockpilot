using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Pos;
using backend_stockpilot.Models;
using backend_stockpilot.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace StockPilot.IntegrationTests.POS;

public partial class PosIntegrationTests
{
    [Fact]
    public async Task Checkout_UsesStoreTaxRateAndCurrency()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var store = await db.Stores.SingleAsync(s => s.Id == fixture.StoreId);

            store.TaxRate = 5m;
            store.Currency = "USD";

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            0m,
            "Card",
            null
        );

        var response = await client.PostAsJsonAsync("/api/pos/checkout", request);
        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        Assert.Equal(200m, result.Receipt.Subtotal);
        Assert.Equal(10m, result.Receipt.Tax);
        Assert.Equal(210m, result.Receipt.Total);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb =
            verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sale = await verificationDb.Sales
            .SingleAsync(s => s.Id == result.Receipt.SaleId);

        var payment = await verificationDb.Payments
            .SingleAsync(p => p.SaleId == sale.Id);

        Assert.Equal(210m, payment.Amount);
        Assert.Equal("USD", payment.Currency);
        Assert.Equal(fixture.StoreId, payment.StoreId);
    }

    [Fact]
    public async Task Checkout_GeneratesSaleNumberUsingStoreCodeAndDate()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = CreateBusinessRulesClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout",
            CreateBusinessRulesRequest(fixture, 1));

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var store = await db.Stores.SingleAsync(s => s.Id == fixture.StoreId);
        var sale = await db.Sales.SingleAsync(s => s.Id == result.Receipt.SaleId);

        Assert.Matches(
            new Regex($"^SP-{Regex.Escape(store.Code)}-\\d{{6}}-\\d{{4}}$"),
            sale.SaleNumber);
    }

    [Fact]
    public async Task Checkout_IncrementsSaleNumberSequenceForSameStore()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = CreateBusinessRulesClient(fixture.Token);

        var firstResponse = await client.PostAsJsonAsync(
            "/api/pos/checkout",
            CreateBusinessRulesRequest(fixture, 1));

        var firstResult =
            await firstResponse.Content.ReadFromJsonAsync<CheckoutResponse>();

        var secondResponse = await client.PostAsJsonAsync(
            "/api/pos/checkout",
            CreateBusinessRulesRequest(fixture, 1));

        var secondResult =
            await secondResponse.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var firstSale = await db.Sales.SingleAsync(
            s => s.Id == firstResult.Receipt.SaleId);

        var secondSale = await db.Sales.SingleAsync(
            s => s.Id == secondResult.Receipt.SaleId);

        Assert.NotEqual(firstSale.SaleNumber, secondSale.SaleNumber);

        var firstSequence = int.Parse(firstSale.SaleNumber[^4..]);
        var secondSequence = int.Parse(secondSale.SaleNumber[^4..]);

        Assert.Equal(firstSequence + 1, secondSequence);
    }

    [Fact]
    public async Task Checkout_RecordsCashierIdentityOnSale()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = CreateBusinessRulesClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout",
            CreateBusinessRulesRequest(fixture, 1));

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cashier = await db.Users.SingleAsync(
            u => u.StoreId == fixture.StoreId &&
                 u.Role == UserRole.Cashier);

        var sale = await db.Sales.SingleAsync(
            s => s.Id == result.Receipt.SaleId);

        Assert.Equal(cashier.Id, sale.CashierId);
        Assert.Equal("Test Cashier", sale.CashierName);
    }

    [Fact]
    public async Task Checkout_RecordsCorrectActorAndRoleInAuditLog()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = CreateBusinessRulesClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout",
            CreateBusinessRulesRequest(fixture, 1));

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cashier = await db.Users.SingleAsync(
            u => u.StoreId == fixture.StoreId &&
                 u.Role == UserRole.Cashier);

        var sale = await db.Sales.SingleAsync(
            s => s.Id == result.Receipt.SaleId);

        var audit = await db.AuditLogs.SingleAsync(
            a => a.EntityId == sale.SaleNumber);

        Assert.Equal(fixture.StoreId, audit.StoreId);
        Assert.Equal(cashier.Id, audit.ActorId);
        Assert.Equal("Test Cashier", audit.ActorName);
        Assert.Equal(UserRole.Cashier, audit.ActorRole);
        Assert.Equal("Sale completed", audit.Action);
        Assert.Equal("Sale", audit.Entity);
        Assert.Equal(AuditOutcome.Success, audit.Outcome);
    }

    [Fact]
    public async Task Checkout_OnlyDeductsInventoryFromCashiersStore()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var secondStore = await CreateBusinessRulesStoreAsync(
            fixture.ProductId,
            initialStock: 30);

        using var client = CreateBusinessRulesClient(secondStore.Token);

        // A cashier belonging to the second store checks out.
        // The requested StoreId is deliberately the first store.
        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto> { new(fixture.ProductId, 2) },
            0m,
            "Cash",
            250m
        );

        var response = await client.PostAsJsonAsync("/api/pos/checkout", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var firstInventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        var secondInventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == secondStore.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(20, firstInventory.QuantityOnHand);
        Assert.Equal(28, secondInventory.QuantityOnHand);

        var sale = await db.Sales
            .OrderByDescending(s => s.Id)
            .FirstAsync();

        Assert.Equal(secondStore.StoreId, sale.StoreId);
    }

    [Fact]
    public async Task Checkout_CreatesIndependentSalesForDifferentStores()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        var secondStore = await CreateBusinessRulesStoreAsync(
            fixture.ProductId,
            initialStock: 30);

        using var firstClient = CreateBusinessRulesClient(fixture.Token);
        using var secondClient = CreateBusinessRulesClient(secondStore.Token);

        var firstTask = firstClient.PostAsJsonAsync(
            "/api/pos/checkout",
            CreateBusinessRulesRequest(fixture, 2));

        var secondTask = secondClient.PostAsJsonAsync(
            "/api/pos/checkout",
            new CheckoutRequest(
                secondStore.StoreId,
                new List<CheckoutItemDto> { new(fixture.ProductId, 3) },
                0m,
                "Cash",
                500m));

        var responses = await Task.WhenAll(firstTask, secondTask);

        Assert.All(responses, response =>
            Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var firstInventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        var secondInventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == secondStore.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(18, firstInventory.QuantityOnHand);
        Assert.Equal(27, secondInventory.QuantityOnHand);

        Assert.Equal(
            1,
            await db.Sales.CountAsync(s => s.StoreId == fixture.StoreId));

        Assert.Equal(
            1,
            await db.Sales.CountAsync(s => s.StoreId == secondStore.StoreId));
    }

    private HttpClient CreateBusinessRulesClient(string token)
    {
        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static CheckoutRequest CreateBusinessRulesRequest(
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

    private async Task<(int StoreId, string Token)> CreateBusinessRulesStoreAsync(
        int productId,
        int initialStock)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var store = new Store
        {
            Code = $"T{Guid.NewGuid():N}"[..8],
            Name = "Second POS Integration Store",
            Currency = "PHP",
            TaxRate = 12m,
            TimeZone = "Asia/Manila",
            IsActive = true
        };

        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var cashier = new User
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

        db.Users.Add(cashier);
        await db.SaveChangesAsync();

        db.InventoryLevels.Add(new InventoryLevel
        {
            StoreId = store.Id,
            ProductId = productId,
            QuantityOnHand = initialStock
        });

        await db.SaveChangesAsync();

        var tokenService =
            scope.ServiceProvider.GetRequiredService<ITokenService>();

        var (token, _) = tokenService.CreateToken(cashier);

        return (store.Id, token);
    }
}