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
    public async Task Checkout_ConcurrentRequestsWithSufficientStock_BothSucceed()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        using var firstClient = CreateConcurrencyClient(fixture.Token);
        using var secondClient = CreateConcurrencyClient(fixture.Token);

        var firstRequest = CreateConcurrencyRequest(fixture, quantity: 2);
        var secondRequest = CreateConcurrencyRequest(fixture, quantity: 2);

        var firstTask = firstClient.PostAsJsonAsync(
            "/api/pos/checkout", firstRequest);

        var secondTask = secondClient.PostAsJsonAsync(
            "/api/pos/checkout", secondRequest);

        var responses = await Task.WhenAll(firstTask, secondTask);

        Assert.All(responses, response =>
            Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        var sales = await db.Sales
            .Where(s => s.StoreId == fixture.StoreId)
            .ToListAsync();

        Assert.Equal(16, inventory.QuantityOnHand);
        Assert.Equal(2, sales.Count);

        // Both concurrent checkouts must receive different sale numbers.
        Assert.Equal(2, sales.Select(s => s.SaleNumber).Distinct().Count());

        // The store-scoped sequence should contain two different values.
        var sequences = sales
            .Select(s => int.Parse(s.SaleNumber[^4..]))
            .OrderBy(value => value)
            .ToList();

        Assert.Equal(2, sequences.Count);
        Assert.Equal(sequences[0] + 1, sequences[1]);

        Assert.Equal(
            2,
            await db.StockMovements.CountAsync(
                m => m.StoreId == fixture.StoreId &&
                     m.ProductId == fixture.ProductId &&
                     m.MovementType == MovementType.Sale));
    }

    [Fact]
    public async Task Checkout_CompetingRequestsCannotOversellInventory()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        await using (var setupScope = _factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var inventory = await db.InventoryLevels.SingleAsync(
                i => i.StoreId == fixture.StoreId &&
                     i.ProductId == fixture.ProductId);

            inventory.QuantityOnHand = 1;
            await db.SaveChangesAsync();
        }

        using var firstClient = CreateConcurrencyClient(fixture.Token);
        using var secondClient = CreateConcurrencyClient(fixture.Token);

        var firstTask = firstClient.PostAsJsonAsync(
            "/api/pos/checkout",
            CreateConcurrencyRequest(fixture, quantity: 1));

        var secondTask = secondClient.PostAsJsonAsync(
            "/api/pos/checkout",
            CreateConcurrencyRequest(fixture, quantity: 1));

        var responses = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb =
            verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inventoryAfterCheckout =
            await verificationDb.InventoryLevels.SingleAsync(
                i => i.StoreId == fixture.StoreId &&
                     i.ProductId == fixture.ProductId);

        Assert.Equal(0, inventoryAfterCheckout.QuantityOnHand);

        Assert.Equal(
            1,
            await verificationDb.Sales.CountAsync(
                s => s.StoreId == fixture.StoreId));

        Assert.Equal(
            1,
            await verificationDb.StockMovements.CountAsync(
                m => m.StoreId == fixture.StoreId &&
                     m.ProductId == fixture.ProductId &&
                     m.MovementType == MovementType.Sale));
    }

    [Fact]
    public async Task Checkout_ConcurrentRequestsGenerateUniqueSaleNumbers()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        const int requestCount = 4;

        var tasks = Enumerable.Range(0, requestCount)
            .Select(async _ =>
            {
                using var client = CreateConcurrencyClient(fixture.Token);

                return await client.PostAsJsonAsync(
                    "/api/pos/checkout",
                    CreateConcurrencyRequest(fixture, quantity: 1));
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, response =>
            Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sales = await db.Sales
            .Where(s => s.StoreId == fixture.StoreId)
            .OrderBy(s => s.SaleNumber)
            .ToListAsync();

        Assert.Equal(requestCount, sales.Count);
        Assert.Equal(
            requestCount,
            sales.Select(s => s.SaleNumber).Distinct().Count());

        var sequences = sales
            .Select(s => int.Parse(s.SaleNumber[^4..]))
            .OrderBy(value => value)
            .ToList();

        Assert.Equal(
            Enumerable.Range(sequences[0], requestCount),
            sequences);

        var inventory = await db.InventoryLevels.SingleAsync(
            i => i.StoreId == fixture.StoreId &&
                 i.ProductId == fixture.ProductId);

        Assert.Equal(16, inventory.QuantityOnHand);
    }

    [Fact]
    public async Task Checkout_ReturnsStockChangeForSuccessfulCheckout()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        using var client = CreateConcurrencyClient(fixture.Token);

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout",
            CreateConcurrencyRequest(fixture, quantity: 3));

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        var stockChange = Assert.Single(result.StockChanges);

        Assert.Equal(fixture.ProductId, stockChange.ProductId);
        Assert.Equal(17, stockChange.Quantity);
    }

    [Fact]
    public async Task Checkout_WithMultipleProducts_ReturnsStockChangeForEachProduct()
    {
        var fixture = await CreateCheckoutFixtureAsync();

        int secondProductId;

        await using (var setupScope = _factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var secondProduct = new Product
            {
                Name = "Second Concurrency Test Product",
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

        using var client = CreateConcurrencyClient(fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
                new(fixture.ProductId, 2),
                new(secondProductId, 3)
            },
            0m,
            "Cash",
            1000m
        );

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout", request);

        var result = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.StockChanges.Count);

        Assert.Contains(
            result.StockChanges,
            change => change.ProductId == fixture.ProductId &&
                      change.Quantity == 18);

        Assert.Contains(
            result.StockChanges,
            change => change.ProductId == secondProductId &&
                      change.Quantity == 7);
    }

    private HttpClient CreateConcurrencyClient(string token)
    {
        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static CheckoutRequest CreateConcurrencyRequest(
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
}