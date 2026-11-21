using System.Net;
using backend_stockpilot.Models;

namespace StockPilot.IntegrationTests;

public partial class SalesIntegrationTests
{
    [Fact]
    public async Task Access_CashierOnlySeesTheirOwnSales()
    {
        var s = await NewScenarioAsync();
        var otherCashier = await CreateUserAsync(UserRole.Cashier, s.Store.Id);
        var product = await CreateProductAsync();

        for (var i = 0; i < 2; i++)
            await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)]);
        for (var i = 0; i < 3; i++)
            await CreateSaleAsync(s.Store, otherCashier, [new Line(product, 1, 100m)]);

        using var client = ClientFor(s.Cashier);
        var page = await ListAsync(client);

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, item => Assert.Equal(s.Cashier.Name, item.CashierName));
    }

    [Fact]
    public async Task Access_ManagerOnlySeesTheirStoresSales()
    {
        var storeA = await NewScenarioAsync();
        var storeB = await NewScenarioAsync();
        var secondCashierInA = await CreateUserAsync(UserRole.Cashier, storeA.Store.Id);
        var product = await CreateProductAsync();

        await CreateSaleAsync(storeA.Store, storeA.Cashier, [new Line(product, 1, 100m)]);
        await CreateSaleAsync(storeA.Store, storeA.Cashier, [new Line(product, 1, 100m)]);
        await CreateSaleAsync(storeA.Store, secondCashierInA, [new Line(product, 1, 100m)]);
        await CreateSaleAsync(storeB.Store, storeB.Cashier, [new Line(product, 1, 100m)]);
        await CreateSaleAsync(storeB.Store, storeB.Cashier, [new Line(product, 1, 100m)]);

        using var managerA = ClientFor(storeA.Manager);
        using var managerB = ClientFor(storeB.Manager);

        Assert.Equal(3, (await ListAsync(managerA)).TotalCount);
        Assert.Equal(2, (await ListAsync(managerB)).TotalCount);
    }

    [Fact]
    public async Task Access_ManagerWithoutStoreClaimSeesNothing()
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)]);
        var storelessManager = await CreateUserAsync(UserRole.Manager, null);

        using var client = ClientFor(storelessManager);
        var page = await ListAsync(client);

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Access_OwnerCanAccessSalesWithinTheSelectedStoreContext()
    {
        var storeA = await NewScenarioAsync();
        var storeB = await NewScenarioAsync();
        var product = await CreateProductAsync();
        var saleA = await CreateSaleAsync(storeA.Store, storeA.Cashier, [new Line(product, 1, 100m)]);
        var saleB = await CreateSaleAsync(storeB.Store, storeB.Cashier, [new Line(product, 1, 100m)]);

        using (var inStoreA = ClientFor(storeA.Owner, storeContext: storeA.Store.Id))
        {
            Assert.Equal(1, (await ListAsync(inStoreA, $"?search={saleA.SaleNumber}")).TotalCount);
            Assert.Equal(0, (await ListAsync(inStoreA, $"?search={saleB.SaleNumber}")).TotalCount);
        }

        using (var inStoreB = ClientFor(storeA.Owner, storeContext: storeB.Store.Id))
        {
            Assert.Equal(0, (await ListAsync(inStoreB, $"?search={saleA.SaleNumber}")).TotalCount);
            Assert.Equal(1, (await ListAsync(inStoreB, $"?search={saleB.SaleNumber}")).TotalCount);
        }

        using var everywhere = ClientFor(storeA.Owner);
        Assert.Equal(1, (await ListAsync(everywhere, $"?search={saleA.SaleNumber}")).TotalCount);
        Assert.Equal(1, (await ListAsync(everywhere, $"?search={saleB.SaleNumber}")).TotalCount);
    }

    [Fact]
    public async Task Access_CashierCannotReadAnotherCashiersSale()
    {
        var s = await NewScenarioAsync();
        var otherCashier = await CreateUserAsync(UserRole.Cashier, s.Store.Id);
        var product = await CreateProductAsync();
        var sale = await CreateSaleAsync(s.Store, otherCashier, [new Line(product, 1, 100m)]);

        using var client = ClientFor(s.Cashier);
        var response = await client.GetAsync($"/api/sales/{sale.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Access_CashierCannotProcessRefunds_EvenOnTheirOwnSale()
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        var sale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)]);

        using var client = ClientFor(s.Cashier);
        var response = await PostRefundAsync(client, sale.Id, Req("Customer changed mind", false, (ItemId(sale, 0), 1)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var reloaded = await ReloadSaleAsync(sale.Id);
        Assert.Empty(reloaded.Refunds);
    }

    [Fact]
    public async Task Access_UnauthorizedRolesCannotUseTheEndpoint()
    {
        var s = await NewScenarioAsync();
        var clerk = await CreateUserAsync(UserRole.InventoryClerk, s.Store.Id);
        var product = await CreateProductAsync();
        var sale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)]);

        using var client = ClientFor(clerk);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/sales/{sale.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostRefundAsync(client, sale.Id, Req("Nope", false, (ItemId(sale, 0), 1)))).StatusCode);
    }

    [Fact]
    public async Task Access_UnauthenticatedRequestsAreRejected()
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        var sale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)]);

        using var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/sales/{sale.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await PostRefundAsync(anonymous, sale.Id, Req("Nope", false, (ItemId(sale, 0), 1)))).StatusCode);
    }
}
