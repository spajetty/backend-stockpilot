using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Pos;
using backend_stockpilot.Hubs;
using backend_stockpilot.Models;
using backend_stockpilot.Services.Interfaces;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StockPilot.IntegrationTests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace StockPilot.IntegrationTests.POS;

public partial class PosIntegrationTests
{
    [Fact]
    public async Task Checkout_BroadcastsStockChangedToConnectedClient()
    {
        var fixture = await CreateSignalRFixtureAsync(_factory);

        var notificationReceived =
            new TaskCompletionSource<JsonElement>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri("http://localhost/hubs/stock"),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;

                    options.AccessTokenProvider =
                        () => Task.FromResult<string?>(fixture.Token);

                    options.HttpMessageHandlerFactory =
                        _ => _factory.Server.CreateHandler();
                })
            .Build();

        connection.On<JsonElement>(
            "stockChanged",
            payload => notificationReceived.TrySetResult(payload));

        await connection.StartAsync();

        // Join the store's SignalR group before checkout.
        await connection.InvokeAsync("JoinStore", fixture.StoreId);

        using var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Token);

        var request = new CheckoutRequest(
            fixture.StoreId,
            new List<CheckoutItemDto>
            {
                new(fixture.ProductId, 3)
            },
            0m,
            "Cash",
            500m
        );

        var response = await client.PostAsJsonAsync(
            "/api/pos/checkout",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Wait for the actual SignalR message.
        var notification = await notificationReceived.Task.WaitAsync(
            TimeSpan.FromSeconds(10));

        Assert.Equal(JsonValueKind.Array, notification.ValueKind);
        Assert.Equal(1, notification.GetArrayLength());

        var stockChange = notification[0];

        Assert.Equal(
            fixture.ProductId,
            stockChange.GetProperty("productId").GetInt32());

        Assert.Equal(
            17,
            stockChange.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Checkout_WhenSignalRBroadcastFails_StillCommitsSale()
    {
        var hubContext = new Mock<IHubContext<StockHub>>();
        var hubClients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();

        hubClients
            .Setup(clients => clients.Group(It.IsAny<string>()))
            .Returns(clientProxy.Object);

        hubContext
            .SetupGet(context => context.Clients)
            .Returns(hubClients.Object);

        clientProxy
            .Setup(proxy => proxy.SendCoreAsync(
                "stockChanged",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new InvalidOperationException(
                    "Simulated SignalR broadcast failure."));

        var testFactory =
            new SignalRFailureWebApplicationFactory(hubContext.Object);

        await testFactory.InitializeAsync();

        try
        {
            var fixture = await CreateSignalRFixtureAsync(testFactory);

            using var client = testFactory.CreateClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", fixture.Token);

            var request = new CheckoutRequest(
                fixture.StoreId,
                new List<CheckoutItemDto>
                {
                    new(fixture.ProductId, 1)
                },
                0m,
                "Cash",
                500m
            );

            var response = await client.PostAsJsonAsync(
                "/api/pos/checkout",
                request);

            // Checkout should succeed even though notification delivery fails.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await using var scope =
                testFactory.Services.CreateAsyncScope();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var sale = await db.Sales.SingleAsync(
                s => s.StoreId == fixture.StoreId);

            var inventory = await db.InventoryLevels.SingleAsync(
                i => i.StoreId == fixture.StoreId &&
                     i.ProductId == fixture.ProductId);

            Assert.Equal(19, inventory.QuantityOnHand);
            Assert.Equal(100m, sale.Subtotal);
            Assert.Equal(12m, sale.Tax);
            Assert.Equal(112m, sale.Total);

            // Verify the controller attempted the notification.
            clientProxy.Verify(
                proxy => proxy.SendCoreAsync(
                    "stockChanged",
                    It.IsAny<object?[]>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            await testFactory.DisposeAsync();
        }
    }

    private async Task<(int StoreId, int ProductId, string Token)>
        CreateSignalRFixtureAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var store = new Store
        {
            Code = $"T{Guid.NewGuid():N}"[..8],
            Name = "SignalR Integration Test Store",
            Currency = "PHP",
            TaxRate = 12m,
            TimeZone = "Asia/Manila",
            IsActive = true
        };

        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var user = new User
        {
            Email = $"signalr-{Guid.NewGuid():N}@stockpilot.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("testpassword"),
            FirstName = "SignalR",
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
            Name = "SignalR Integration Test Product",
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
}