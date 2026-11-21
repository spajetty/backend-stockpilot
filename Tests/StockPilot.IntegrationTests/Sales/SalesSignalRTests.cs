using System.Net;
using System.Text.Json;
using backend_stockpilot.Hubs;
using backend_stockpilot.Models;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace StockPilot.IntegrationTests;

public partial class SalesIntegrationTests
{
    private static readonly TimeSpan HubWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NoMessageWait = TimeSpan.FromMilliseconds(1500);

    private async Task<(HubConnection Connection, TaskCompletionSource<JsonElement> FirstMessage)> ListenAsync(
        TestUser user, int storeId, WebApplicationFactory<Program>? factory = null)
    {
        var host = factory ?? _factory;
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.Server.BaseAddress, "/hubs/stock"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(Token(user));
            })
            .Build();

        connection.On<JsonElement>("stockChanged", payload => received.TrySetResult(payload));

        await connection.StartAsync();
        await connection.InvokeAsync("JoinStore", storeId);
        return (connection, received);
    }

    private static async Task<JsonElement?> WaitForAsync(TaskCompletionSource<JsonElement> message, TimeSpan timeout)
    {
        var finished = await Task.WhenAny(message.Task, Task.Delay(timeout));
        return finished == message.Task ? await message.Task : null;
    }

    private static (int ProductId, int Quantity) ReadChange(JsonElement payload, int index)
    {
        var properties = payload[index].EnumerateObject().ToList();
        return (properties[0].Value.GetInt32(), properties[1].Value.GetInt32());
    }

    [Fact]
    public async Task SignalR_BroadcastsStockChangedWhenInventoryIsRestored()
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        await SetStockAsync(s.Store.Id, product.Id, 10);
        var otherStore = await NewScenarioAsync();

        var (connection, message) = await ListenAsync(s.Manager, s.Store.Id);
        await using var _ = connection;
        var (outsiderConnection, outsiderMessage) = await ListenAsync(otherStore.Manager, otherStore.Store.Id);
        await using var __ = outsiderConnection;

        using var client = ClientFor(s.Manager);
        var response = await PostRefundAsync(client, sale.Id, Req("Unopened", true, (ItemId(sale, 0), 2)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await WaitForAsync(message, HubWait);
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Value.GetArrayLength());
        var change = ReadChange(payload.Value, 0);
        Assert.Equal(product.Id, change.ProductId);
        Assert.Equal(12, change.Quantity);

        Assert.Null(await WaitForAsync(outsiderMessage, NoMessageWait));
    }

    [Fact]
    public async Task SignalR_DoesNotBroadcastWhenInventoryIsNotAffected()
    {
        var s = await NewScenarioAsync();
        var a = await CreateProductAsync();
        var b = await CreateProductAsync();
        var sale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(a, 2, 100m), new Line(b, 2, 100m)]);
        await SetStockAsync(s.Store.Id, a.Id, 5);
        await SetStockAsync(s.Store.Id, b.Id, 5);

        var (connection, message) = await ListenAsync(s.Manager, s.Store.Id);
        await using var _ = connection;
        using var client = ClientFor(s.Manager);

        var noStock = await PostRefundAsync(client, sale.Id, Req("Damaged", false, (ItemId(sale, 0), 1)));
        Assert.Equal(HttpStatusCode.OK, noStock.StatusCode);
        Assert.Null(await WaitForAsync(message, NoMessageWait));

        var withStock = await PostRefundAsync(client, sale.Id, Req("Resellable", true, (ItemId(sale, 1), 1)));
        Assert.Equal(HttpStatusCode.OK, withStock.StatusCode);

        var payload = await WaitForAsync(message, HubWait);
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Value.GetArrayLength());
        Assert.Equal(b.Id, ReadChange(payload.Value, 0).ProductId);
    }

    [Fact]
    public async Task SignalR_DoesNotBroadcastWhenRefundedItemsHaveNoProduct()
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        var orphanSale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(null, 2, 50m)]);
        var normalSale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)]);
        await SetStockAsync(s.Store.Id, product.Id, 5);

        var (connection, message) = await ListenAsync(s.Manager, s.Store.Id);
        await using var _ = connection;
        using var client = ClientFor(s.Manager);

        var orphanRefund = await PostRefundAsync(client, orphanSale.Id, Req("Product gone", true, (ItemId(orphanSale, 0), 1)));
        Assert.Equal(HttpStatusCode.OK, orphanRefund.StatusCode);
        Assert.Null(await WaitForAsync(message, NoMessageWait));

        var normalRefund = await PostRefundAsync(client, normalSale.Id, Req("Resellable", true, (ItemId(normalSale, 0), 1)));
        Assert.Equal(HttpStatusCode.OK, normalRefund.StatusCode);
        Assert.NotNull(await WaitForAsync(message, HubWait));
    }

    [Fact]
    public async Task SignalR_BroadcastFailureDoesNotUndoACommittedRefund()
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        await SetStockAsync(s.Store.Id, product.Id, 10);

        var proxy = new Mock<IClientProxy>();
        proxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated hub failure."));
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hubContext = new Mock<IHubContext<StockHub>>();
        hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

        await using var brokenHub = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(hubContext.Object)));

        using var client = ClientFor(s.Manager, factory: brokenHub);
        var response = await PostRefundAsync(client, sale.Id, Req("Hub is down", true, (ItemId(sale, 0), 2)));

        proxy.Verify(
            p => p.SendCoreAsync("stockChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var refund = Assert.Single(await GetRefundsAsync(sale.Id));
        Assert.Equal(201.60m, refund.Amount);
        Assert.Equal(12, await GetStockAsync(s.Store.Id, product.Id));
        Assert.Equal(SalePaymentStatus.PartiallyRefunded, (await ReloadSaleAsync(sale.Id)).PaymentStatus);
        Assert.Single(await GetAuditLogsAsync(sale.SaleNumber));
    }
}
