using System.Net;
using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;

namespace StockPilot.IntegrationTests;

public partial class SalesIntegrationTests
{
    // 4 x 100, 40 discount, 12% VAT -> total 403.20, so one unit refunds 100.80
    private async Task<(Scenario S, Product Product, Sale Sale)> NewDiscountedSaleAsync(
        SaleStatus status = SaleStatus.Completed)
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync(price: 100m);
        var sale = await CreateSaleAsync(
            s.Store, s.Cashier, [new Line(product, 4, 100m)], discount: 40m, status: status);
        Assert.Equal(403.20m, sale.Total);
        return (s, product, sale);
    }

    private async Task<List<Refund>> GetRefundsAsync(int saleId)
    {
        await using var db = NewDb();
        return await db.Refunds.AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.SaleId == saleId)
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task Refund_ProcessesAPartialRefund()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, sale.Id, Req("Wrong size", false, (ItemId(sale, 0), 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadAsync<backend_stockpilot.Dtos.Sales.SaleDetailDto>(response);
        Assert.Equal("PartiallyRefunded", detail.Status);
        Assert.Equal(100.80m, detail.RefundedTotal);
        Assert.Equal(1, detail.Items.Single().RefundedQuantity);
        Assert.Single(detail.Refunds);

        var refunds = await GetRefundsAsync(sale.Id);
        var refund = Assert.Single(refunds);
        Assert.Equal(100.80m, refund.Amount);
        Assert.Equal("Wrong size", refund.Reason);
        Assert.Equal(s.Manager.Id, refund.ProcessedById);
        Assert.Equal(s.Manager.Name, refund.ProcessedName);
        Assert.Equal(s.Store.Id, refund.StoreId);
        var line = Assert.Single(refund.Items);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(100.80m, line.Amount);
    }

    [Fact]
    public async Task Refund_ProcessesAFullRefund()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, sale.Id, Req("Order cancelled", false, (ItemId(sale, 0), 4)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadAsync<backend_stockpilot.Dtos.Sales.SaleDetailDto>(response);
        Assert.Equal("Refunded", detail.Status);
        Assert.Equal(403.20m, detail.RefundedTotal);
        Assert.Equal(4, detail.Items.Single().RefundedQuantity);

        var reloaded = await ReloadSaleAsync(sale.Id);
        Assert.Equal(SalePaymentStatus.Refunded, reloaded.PaymentStatus);
        Assert.Equal(sale.Total, reloaded.Refunds.Sum(r => r.Amount));
    }

    [Fact]
    public async Task Refund_OwnerCanRefundToo()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var owner = ClientFor(s.Owner);

        var response = await PostRefundAsync(owner, sale.Id, Req("Owner override", false, (ItemId(sale, 0), 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refund_RejectsUnknownSales()
    {
        var s = await NewScenarioAsync();
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, int.MaxValue, Req("Whatever", false, (1, 1)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refund_RejectsVoidedSales()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync(SaleStatus.Voided);
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, sale.Id, Req("Too late", false, (ItemId(sale, 0), 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("voided", await ReadMessageAsync(response));
        Assert.Empty(await GetRefundsAsync(sale.Id));
    }

    [Fact]
    public async Task Refund_RejectsQuantityAboveWhatIsLeft()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);
        var itemId = ItemId(sale, 0);

        Assert.Equal(HttpStatusCode.OK, (await PostRefundAsync(client, sale.Id, Req("First", false, (itemId, 3)))).StatusCode);

        var response = await PostRefundAsync(client, sale.Id, Req("Second", false, (itemId, 2)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Only 1 of", await ReadMessageAsync(response));
        Assert.Single(await GetRefundsAsync(sale.Id));
    }

    [Fact]
    public async Task Refund_RejectsMoreThanWasSoldInOneRequest()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, sale.Id, Req("Too many", false, (ItemId(sale, 0), 5)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await GetRefundsAsync(sale.Id));
    }

    [Fact]
    public async Task Refund_RejectsItemsBelongingToAnotherSale()
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        var otherSale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 2, 100m)]);
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, sale.Id, Req("Mixed up", false, (ItemId(otherSale, 0), 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("isn't part of this sale", await ReadMessageAsync(response));
        Assert.Empty(await GetRefundsAsync(sale.Id));
        Assert.Empty(await GetRefundsAsync(otherSale.Id));
    }

    [Fact]
    public async Task Refund_RejectsRequestingTheSameSaleAgainOnceItIsFullyRefunded()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);
        var request = Req("Full", false, (ItemId(sale, 0), 4));

        Assert.Equal(HttpStatusCode.OK, (await PostRefundAsync(client, sale.Id, request)).StatusCode);
        var duplicate = await PostRefundAsync(client, sale.Id, request);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("already fully refunded", await ReadMessageAsync(duplicate));
        Assert.Single(await GetRefundsAsync(sale.Id));
    }

    [Fact]
    public async Task Refund_MergesDuplicateItemEntriesInOneRequest()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);
        var itemId = ItemId(sale, 0);

        var response = await PostRefundAsync(client, sale.Id, Req("Split entries", false, (itemId, 2), (itemId, 2)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refund = Assert.Single(await GetRefundsAsync(sale.Id));
        var line = Assert.Single(refund.Items);
        Assert.Equal(4, line.Quantity);
        Assert.Equal(403.20m, refund.Amount);
    }

    [Fact]
    public async Task Refund_RejectsDuplicateEntriesThatTogetherExceedTheRemainingQuantity()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);
        var itemId = ItemId(sale, 0);

        var response = await PostRefundAsync(client, sale.Id, Req("Sneaky", false, (itemId, 3), (itemId, 2)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await GetRefundsAsync(sale.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refund_RejectsEmptyReasons(string? reason)
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, sale.Id, Req(reason, false, (ItemId(sale, 0), 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (reason is not null)
            Assert.Equal("A reason is required.", await ReadMessageAsync(response));
        Assert.Empty(await GetRefundsAsync(sale.Id));
    }

    [Fact]
    public async Task Refund_RejectsReasonsLongerThan500Characters_ButAcceptsExactly500()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);
        var itemId = ItemId(sale, 0);

        var tooLong = await PostRefundAsync(client, sale.Id, Req(new string('x', 501), false, (itemId, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("too long", await ReadMessageAsync(tooLong));
        Assert.Empty(await GetRefundsAsync(sale.Id));

        var exact = await PostRefundAsync(client, sale.Id, Req(new string('x', 500), false, (itemId, 1)));
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        var refund = Assert.Single(await GetRefundsAsync(sale.Id));
        Assert.Equal(500, refund.Reason.Length);
        var log = Assert.Single(await GetAuditLogsAsync(sale.SaleNumber));
        Assert.True(log.Summary.Length <= 500);
    }

    [Fact]
    public async Task Refund_TrimsTheReasonBeforeSavingAndCountingIt()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);

        var padded = "  " + new string('y', 500) + "  ";
        var response = await PostRefundAsync(client, sale.Id, Req(padded, false, (ItemId(sale, 0), 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500, (await GetRefundsAsync(sale.Id)).Single().Reason.Length);
    }

    [Fact]
    public async Task Refund_RejectsRequestsWithoutValidItems()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);
        var itemId = ItemId(sale, 0);

        foreach (var request in new[]
                 {
                     Req("No items", false),
                     Req("Zero quantity", false, (itemId, 0)),
                     Req("Negative quantity", false, (itemId, -1)),
                 })
        {
            var response = await PostRefundAsync(client, sale.Id, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("at least one item", await ReadMessageAsync(response));
        }

        Assert.Empty(await GetRefundsAsync(sale.Id));
    }

    [Fact]
    public async Task Refund_CalculatesAmountsWithDiscountsAndVat()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);
        var itemId = ItemId(sale, 0);

        await PostRefundAsync(client, sale.Id, Req("One", false, (itemId, 1)));
        await PostRefundAsync(client, sale.Id, Req("Two", false, (itemId, 2)));

        var refunds = await GetRefundsAsync(sale.Id);
        Assert.Equal(100.80m, refunds[0].Amount);
        Assert.Equal(201.60m, refunds[1].Amount);
    }

    [Fact]
    public async Task Refund_SettlesTheFinalRefundToTheRemainingTotal()
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        // 3 units for 100 + 12% VAT = 112; 37.33 per unit would leave a centavo
        var sale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 3, 33.33m, LineTotal: 100m)]);
        Assert.Equal(112m, sale.Total);

        using var client = ClientFor(s.Manager);
        var itemId = ItemId(sale, 0);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await PostRefundAsync(client, sale.Id, Req($"Unit {i + 1}", false, (itemId, 1)))).StatusCode);

        var refunds = await GetRefundsAsync(sale.Id);
        Assert.Equal(new[] { 37.33m, 37.33m, 37.34m }, refunds.Select(r => r.Amount).ToArray());
        Assert.Equal(112m, refunds.Sum(r => r.Amount));
        Assert.Equal(SalePaymentStatus.Refunded, (await ReloadSaleAsync(sale.Id)).PaymentStatus);
    }

    [Fact]
    public async Task Refund_UpdatesThePaymentStatus()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);
        var itemId = ItemId(sale, 0);

        Assert.Equal(SalePaymentStatus.Paid, (await ReloadSaleAsync(sale.Id)).PaymentStatus);

        await PostRefundAsync(client, sale.Id, Req("Part", false, (itemId, 1)));
        var afterPartial = await ReloadSaleAsync(sale.Id);
        Assert.Equal(SalePaymentStatus.PartiallyRefunded, afterPartial.PaymentStatus);
        Assert.Equal(SaleStatus.Completed, afterPartial.Status);

        await PostRefundAsync(client, sale.Id, Req("Rest", false, (itemId, 3)));
        var afterFull = await ReloadSaleAsync(sale.Id);
        Assert.Equal(SalePaymentStatus.Refunded, afterFull.PaymentStatus);
        Assert.Equal(SaleStatus.Completed, afterFull.Status);
    }

    [Theory]
    [InlineData(5, 4, 1)]
    [InlineData(4, 2, 2)]
    public async Task Refund_PreventsOverRefundingUnderConcurrentRequests(int parallel, int quantityEach, int expectedSuccesses)
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        await SetStockAsync(s.Store.Id, product.Id, 10);
        var itemId = ItemId(sale, 0);

        var statuses = await Task.WhenAll(Enumerable.Range(0, parallel).Select(async n =>
        {
            using var client = ClientFor(s.Manager);
            var response = await PostRefundAsync(client, sale.Id, Req($"Concurrent {n}", true, (itemId, quantityEach)));
            return response.StatusCode;
        }));

        Assert.Equal(expectedSuccesses, statuses.Count(x => x == HttpStatusCode.OK));
        Assert.Equal(parallel - expectedSuccesses, statuses.Count(x => x == HttpStatusCode.BadRequest));

        var refunds = await GetRefundsAsync(sale.Id);
        Assert.Equal(expectedSuccesses, refunds.Count);
        Assert.Equal(sale.Total, refunds.Sum(r => r.Amount));
        Assert.Equal(4, refunds.SelectMany(r => r.Items).Sum(i => i.Quantity));
        Assert.Equal(14, await GetStockAsync(s.Store.Id, product.Id));
        Assert.Equal(SalePaymentStatus.Refunded, (await ReloadSaleAsync(sale.Id)).PaymentStatus);
    }
}
