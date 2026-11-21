using System.Net;
using backend_stockpilot.Models;

namespace StockPilot.IntegrationTests;

public partial class SalesIntegrationTests
{
    [Fact]
    public async Task Details_ReturnsSaleWithItemsAndRefunds()
    {
        var s = await NewScenarioAsync();
        var a = await CreateProductAsync(price: 100m);
        var b = await CreateProductAsync(price: 50m);
        var sale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(a, 3, 100m), new Line(b, 1, 50m)]);

        using var manager = ClientFor(s.Manager);
        var refundResponse = await PostRefundAsync(manager, sale.Id, Req("Damaged on delivery", false, (ItemId(sale, 0), 1)));
        Assert.Equal(HttpStatusCode.OK, refundResponse.StatusCode);

        using var cashier = ClientFor(s.Cashier);
        var detail = await GetDetailAsync(cashier, sale.Id);

        Assert.Equal(sale.Id, detail.Id);
        Assert.Equal(sale.SaleNumber, detail.SaleNumber);
        Assert.Equal(s.Cashier.Name, detail.CashierName);
        Assert.Equal("Cash", detail.PaymentMethod);
        Assert.Equal("PartiallyRefunded", detail.PaymentStatus);
        Assert.Equal("PartiallyRefunded", detail.Status);
        Assert.Equal(350m, detail.Subtotal);
        Assert.Equal(0m, detail.Discount);
        Assert.Equal(42m, detail.Tax);
        Assert.Equal(392m, detail.Total);
        Assert.Equal(112m, detail.RefundedTotal);

        Assert.Equal(2, detail.Items.Count);
        var first = detail.Items[0];
        Assert.Equal(a.Name, first.Name);
        Assert.Equal(a.Sku, first.Sku);
        Assert.Equal(3, first.Quantity);
        Assert.Equal(1, first.RefundedQuantity);
        Assert.Equal(100m, first.UnitPrice);
        Assert.Equal(300m, first.LineTotal);
        Assert.Equal(0, detail.Items[1].RefundedQuantity);

        var refund = Assert.Single(detail.Refunds);
        Assert.Equal(112m, refund.Amount);
        Assert.Equal("Damaged on delivery", refund.Reason);
        Assert.Equal(s.Manager.Name, refund.ProcessedBy);
    }

    [Fact]
    public async Task Details_ReturnsVoidedStatusForVoidedSales()
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        var sale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)], status: SaleStatus.Voided);

        using var client = ClientFor(s.Manager);
        var detail = await GetDetailAsync(client, sale.Id);

        Assert.Equal("Voided", detail.Status);
        Assert.Empty(detail.Refunds);
    }

    [Fact]
    public async Task Details_ReturnsNotFoundForNonexistentSales()
    {
        var s = await NewScenarioAsync();
        using var client = ClientFor(s.Manager);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sales/{int.MaxValue}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/sales/not-a-number")).StatusCode);
    }

    [Fact]
    public async Task Details_DoesNotExposeSalesOutsideTheUsersScope()
    {
        var storeA = await NewScenarioAsync();
        var storeB = await NewScenarioAsync();
        var otherCashierInA = await CreateUserAsync(UserRole.Cashier, storeA.Store.Id);
        var product = await CreateProductAsync();
        var saleInA = await CreateSaleAsync(storeA.Store, storeA.Cashier, [new Line(product, 1, 100m)]);

        using (var manager = ClientFor(storeA.Manager))
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/sales/{saleInA.Id}")).StatusCode);

        using (var otherStoreManager = ClientFor(storeB.Manager))
            Assert.Equal(HttpStatusCode.NotFound, (await otherStoreManager.GetAsync($"/api/sales/{saleInA.Id}")).StatusCode);

        using (var otherCashier = ClientFor(otherCashierInA))
            Assert.Equal(HttpStatusCode.NotFound, (await otherCashier.GetAsync($"/api/sales/{saleInA.Id}")).StatusCode);

        using (var otherStoreCashier = ClientFor(storeB.Cashier))
            Assert.Equal(HttpStatusCode.NotFound, (await otherStoreCashier.GetAsync($"/api/sales/{saleInA.Id}")).StatusCode);

        using (var ownerInB = ClientFor(storeA.Owner, storeContext: storeB.Store.Id))
            Assert.Equal(HttpStatusCode.NotFound, (await ownerInB.GetAsync($"/api/sales/{saleInA.Id}")).StatusCode);

        using (var ownerInA = ClientFor(storeA.Owner, storeContext: storeA.Store.Id))
            Assert.Equal(HttpStatusCode.OK, (await ownerInA.GetAsync($"/api/sales/{saleInA.Id}")).StatusCode);
    }
}
