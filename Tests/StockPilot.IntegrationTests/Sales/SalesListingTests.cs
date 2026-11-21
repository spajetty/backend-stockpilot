using System.Net;
using backend_stockpilot.Models;

namespace StockPilot.IntegrationTests;

public partial class SalesIntegrationTests
{
    [Fact]
    public async Task Listing_ReturnsPaginatedSales()
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        for (var i = 0; i < 7; i++)
            await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)], createdAt: DateTime.UtcNow.AddMinutes(-i));

        using var client = ClientFor(s.Manager);

        var defaults = await ListAsync(client);
        Assert.Equal(5, defaults.PageSize);
        Assert.Equal(5, defaults.Items.Count);

        var page1 = await ListAsync(client, "?page=1&pageSize=5");
        Assert.Equal(5, page1.Items.Count);
        Assert.Equal(7, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(1, page1.Page);

        var page2 = await ListAsync(client, "?page=2&pageSize=5");
        Assert.Equal(2, page2.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));

        var clamped = await ListAsync(client, "?page=99&pageSize=5");
        Assert.Equal(2, clamped.Page);
        Assert.Equal(2, clamped.Items.Count);
    }

    [Fact]
    public async Task Listing_ReturnsUnitCountsAndTotals()
    {
        var s = await NewScenarioAsync();
        var a = await CreateProductAsync(price: 100m);
        var b = await CreateProductAsync(price: 50m);
        var sale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(a, 2, 100m), new Line(b, 3, 50m)]);

        using var client = ClientFor(s.Manager);
        var page = await ListAsync(client, $"?search={sale.SaleNumber}");

        var row = Assert.Single(page.Items);
        Assert.Equal(sale.SaleNumber, row.SaleNumber);
        Assert.Equal(5, row.Items);
        Assert.Equal(350m, row.Subtotal);
        Assert.Equal(392m, row.Total);
        Assert.Equal("Cash", row.PaymentMethod);
        Assert.Equal("Completed", row.Status);
    }

    [Theory]
    [InlineData("Cash", PaymentMethod.Cash)]
    [InlineData("card", PaymentMethod.Card)]
    [InlineData("PAYPAL", PaymentMethod.PayPal)]
    public async Task Listing_FiltersByPaymentMethod(string filter, PaymentMethod expected)
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        foreach (var method in Enum.GetValues<PaymentMethod>())
            await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)], method: method);

        using var client = ClientFor(s.Manager);

        var filtered = await ListAsync(client, $"?payment={filter}");
        var row = Assert.Single(filtered.Items);
        Assert.Equal(expected.ToString(), row.PaymentMethod);

        var all = await ListAsync(client, "?payment=all");
        Assert.Equal(Enum.GetValues<PaymentMethod>().Length, all.TotalCount);
    }

    [Theory]
    [InlineData("completed", "Completed")]
    [InlineData("partiallyRefunded", "PartiallyRefunded")]
    [InlineData("refunded", "Refunded")]
    [InlineData("voided", "Voided")]
    public async Task Listing_FiltersByStatus(string filter, string expectedDisplayStatus)
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        Line[] lines = [new Line(product, 1, 100m)];

        await CreateSaleAsync(s.Store, s.Cashier, lines);
        await CreateSaleAsync(s.Store, s.Cashier, lines, payment: SalePaymentStatus.PartiallyRefunded);
        await CreateSaleAsync(s.Store, s.Cashier, lines, payment: SalePaymentStatus.Refunded);
        await CreateSaleAsync(s.Store, s.Cashier, lines, status: SaleStatus.Voided);

        using var client = ClientFor(s.Manager);

        var filtered = await ListAsync(client, $"?status={filter}");
        var row = Assert.Single(filtered.Items);
        Assert.Equal(expectedDisplayStatus, row.Status);

        var all = await ListAsync(client, "?status=all");
        Assert.Equal(4, all.TotalCount);
    }

    [Fact]
    public async Task Listing_SearchesBySaleNumberCashierProductSkuPaymentMethodAndAmount()
    {
        var s = await NewScenarioAsync();
        var cashierB = await CreateUserAsync(UserRole.Cashier, s.Store.Id);
        var productA = await CreateProductAsync("Kopiko Test " + Uid(6));
        var productB = await CreateProductAsync("Nescafe Test " + Uid(6));
        var productC = await CreateProductAsync("Skyflakes Test " + Uid(6));

        var alpha = await CreateSaleAsync(s.Store, s.Cashier, [new Line(productA, 1, 100m)], method: PaymentMethod.Cash);
        var beta = await CreateSaleAsync(s.Store, cashierB, [new Line(productB, 1, 250m)], method: PaymentMethod.Card);
        var gamma = await CreateSaleAsync(s.Store, s.Cashier, [new Line(productC, 1, 77m)], method: PaymentMethod.PayPal);

        using var client = ClientFor(s.Manager);

        async Task AssertOnlyAsync(string term, Sale expected)
        {
            var page = await ListAsync(client, "?search=" + Uri.EscapeDataString(term));
            var row = Assert.Single(page.Items);
            Assert.Equal(expected.Id, row.Id);
        }

        await AssertOnlyAsync(alpha.SaleNumber, alpha);
        await AssertOnlyAsync(beta.SaleNumber[^8..].ToLowerInvariant(), beta);
        await AssertOnlyAsync(cashierB.Name, beta);
        await AssertOnlyAsync(productA.Name, alpha);
        await AssertOnlyAsync(productC.Sku, gamma);
        await AssertOnlyAsync("paypal", gamma);
        await AssertOnlyAsync("86.24", gamma);
        await AssertOnlyAsync("₱280", beta);
        await AssertOnlyAsync("250", beta);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("no-such-thing-" + "zzzz")]
    public async Task Listing_SearchTreatsWildcardsLiterally_AndReturnsEmptyWhenNothingMatches(string term)
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();
        await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)]);

        using var client = ClientFor(s.Manager);
        var page = await ListAsync(client, "?search=" + Uri.EscapeDataString(term));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Theory]
    [InlineData("date", "desc", "CBA")]
    [InlineData("date", "asc", "ABC")]
    [InlineData("total", "desc", "ACB")]
    [InlineData("total", "asc", "BCA")]
    [InlineData("date", "", "CBA")]
    [InlineData("unknown", "desc", "CBA")]
    public async Task Listing_SortsByDateAndTotal(string sortBy, string sortDir, string expectedOrder)
    {
        var s = await NewScenarioAsync();
        var product = await CreateProductAsync();

        var a = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 250m)], createdAt: DateTime.UtcNow.AddDays(-3));
        var b = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 100m)], createdAt: DateTime.UtcNow.AddDays(-2));
        var c = await CreateSaleAsync(s.Store, s.Cashier, [new Line(product, 1, 200m)], createdAt: DateTime.UtcNow.AddDays(-1));
        var byLetter = new Dictionary<char, int> { ['A'] = a.Id, ['B'] = b.Id, ['C'] = c.Id };

        using var client = ClientFor(s.Manager);
        var page = await ListAsync(client, $"?sortBy={sortBy}&sortDir={sortDir}");

        var expectedIds = expectedOrder.Select(ch => byLetter[ch]).ToArray();
        Assert.Equal(expectedIds, page.Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task Listing_RejectsUnknownPaymentMethod()
    {
        var s = await NewScenarioAsync();
        using var client = ClientFor(s.Manager);

        var response = await client.GetAsync("/api/sales?payment=bitcoin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Unknown payment method.", await ReadMessageAsync(response));
    }

    [Fact]
    public async Task Listing_RejectsUnknownStatus()
    {
        var s = await NewScenarioAsync();
        using var client = ClientFor(s.Manager);

        var response = await client.GetAsync("/api/sales?status=archived");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Unknown status.", await ReadMessageAsync(response));
    }

    [Fact]
    public async Task Listing_EnforcesPageSizeLimits()
    {
        var s = await NewScenarioAsync();
        await CreateSalesBulkAsync(s.Store, s.Cashier, 51);

        using var client = ClientFor(s.Manager);

        var zero = await ListAsync(client, "?pageSize=0");
        Assert.Equal(1, zero.PageSize);
        Assert.Single(zero.Items);

        var negative = await ListAsync(client, "?pageSize=-5");
        Assert.Equal(1, negative.PageSize);

        var huge = await ListAsync(client, "?pageSize=1000");
        Assert.Equal(50, huge.PageSize);
        Assert.Equal(50, huge.Items.Count);
        Assert.Equal(51, huge.TotalCount);
        Assert.Equal(2, huge.TotalPages);

        var exact = await ListAsync(client, "?pageSize=50");
        Assert.Equal(50, exact.PageSize);
    }
}
