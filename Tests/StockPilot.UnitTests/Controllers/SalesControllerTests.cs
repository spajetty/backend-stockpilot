using System.Globalization;
using backend_stockpilot.Controllers;
using backend_stockpilot.Models;

namespace StockPilot.UnitTests;

public class SalesControllerTests
{
    // ---------------------------------------------------------------- helpers

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static SaleItem Item(int id, int quantity, decimal lineTotal) => new()
    {
        Id = id,
        ProductId = id,
        ProductName = $"Product {id}",
        Sku = $"SKU-{id}",
        Quantity = quantity,
        UnitPrice = lineTotal / quantity,
        UnitCost = 1m,
        LineTotal = lineTotal,
    };

    private static Sale MakeSale(decimal subtotal, decimal discount, decimal tax, params SaleItem[] items)
    {
        var sale = new Sale
        {
            Id = 1,
            StoreId = 1,
            SaleNumber = "SP-TEST-0001",
            CashierName = "Test Cashier",
            Subtotal = subtotal,
            Discount = discount,
            Tax = tax,
            Total = subtotal - discount + tax,
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = SalePaymentStatus.Paid,
            Status = SaleStatus.Completed,
            CreatedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
        };
        foreach (var item in items) sale.Items.Add(item);
        return sale;
    }

    private static RefundItem RefundLine(int? saleItemId, int quantity, decimal amount) => new()
    {
        SaleItemId = saleItemId,
        ProductId = saleItemId,
        Quantity = quantity,
        Amount = amount,
    };

    private static Refund AddRefund(Sale sale, int id, decimal amount, int minutesAfterSale, params RefundItem[] lines)
    {
        var refund = new Refund
        {
            Id = id,
            StoreId = sale.StoreId,
            SaleId = sale.Id,
            Amount = amount,
            Reason = $"Reason {id}",
            ProcessedName = "Test Manager",
            CreatedAt = sale.CreatedAt.AddMinutes(minutesAfterSale),
        };
        foreach (var line in lines) refund.Items.Add(line);
        sale.Refunds.Add(refund);
        return refund;
    }

    // ---------------------------------------------------------- DisplayStatus

    [Theory]
    [InlineData(SaleStatus.Completed, SalePaymentStatus.Paid, "Completed")]
    [InlineData(SaleStatus.Completed, SalePaymentStatus.PartiallyRefunded, "PartiallyRefunded")]
    [InlineData(SaleStatus.Completed, SalePaymentStatus.Refunded, "Refunded")]
    [InlineData(SaleStatus.Voided, SalePaymentStatus.Paid, "Voided")]
    [InlineData(SaleStatus.Voided, SalePaymentStatus.PartiallyRefunded, "Voided")]
    [InlineData(SaleStatus.Voided, SalePaymentStatus.Refunded, "Voided")]
    public void DisplayStatus_ReturnsCorrectStatus(SaleStatus status, SalePaymentStatus payment, string expected)
    {
        Assert.Equal(expected, SalesController.DisplayStatus(status, payment));
    }

    // ----------------------------------------------------------------- ToDetail

    [Fact]
    public void ToDetail_CalculatesRefundedQuantitiesPerItem()
    {
        var sale = MakeSale(150m, 0m, 18m, Item(1, 5, 100m), Item(2, 2, 50m));
        AddRefund(sale, 1, 22.40m, 10, RefundLine(1, 2, 22.40m));

        var detail = SalesController.ToDetail(sale);

        Assert.Equal(2, detail.Items.Single(i => i.Id == 1).RefundedQuantity);
        Assert.Equal(0, detail.Items.Single(i => i.Id == 2).RefundedQuantity);
    }

    [Fact]
    public void ToDetail_CalculatesRefundedTotals()
    {
        var sale = MakeSale(150m, 0m, 18m, Item(1, 5, 100m), Item(2, 2, 50m));
        AddRefund(sale, 1, 25.50m, 10, RefundLine(1, 1, 25.50m));
        AddRefund(sale, 2, 10.25m, 20, RefundLine(2, 1, 10.25m));

        var detail = SalesController.ToDetail(sale);

        Assert.Equal(35.75m, detail.RefundedTotal);
        Assert.Equal(168m, detail.Total);
        Assert.Equal(2, detail.Refunds.Count);
    }

    [Fact]
    public void ToDetail_HandlesMultipleRefunds()
    {
        var sale = MakeSale(150m, 0m, 18m, Item(1, 5, 100m), Item(2, 2, 50m));
        AddRefund(sale, 1, 22.40m, 10, RefundLine(1, 1, 22.40m));
        AddRefund(sale, 2, 44.80m, 20, RefundLine(1, 2, 44.80m));
        AddRefund(sale, 3, 56.00m, 30, RefundLine(2, 1, 56.00m));

        var detail = SalesController.ToDetail(sale);

        Assert.Equal(3, detail.Items.Single(i => i.Id == 1).RefundedQuantity);
        Assert.Equal(1, detail.Items.Single(i => i.Id == 2).RefundedQuantity);
        Assert.Equal(123.20m, detail.RefundedTotal);
        // newest refund first
        Assert.Equal(new[] { 3, 2, 1 }, detail.Refunds.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void ToDetail_OrdersItemsById_AndMapsHeaderFields()
    {
        var sale = MakeSale(150m, 0m, 18m, Item(2, 2, 50m), Item(1, 5, 100m));
        sale.PaymentStatus = SalePaymentStatus.PartiallyRefunded;

        var detail = SalesController.ToDetail(sale);

        Assert.Equal(new[] { 1, 2 }, detail.Items.Select(i => i.Id).ToArray());
        Assert.Equal("SP-TEST-0001", detail.SaleNumber);
        Assert.Equal("Cash", detail.PaymentMethod);
        Assert.Equal("PartiallyRefunded", detail.PaymentStatus);
        Assert.Equal("PartiallyRefunded", detail.Status);
        Assert.Equal(150m, detail.Subtotal);
        Assert.Equal(18m, detail.Tax);
    }

    [Fact]
    public void ToDetail_IgnoresRefundLinesWithoutSaleItem_ForQuantitiesButCountsTheirAmount()
    {
        var sale = MakeSale(100m, 0m, 12m, Item(1, 4, 100m));
        AddRefund(sale, 1, 28m, 10, RefundLine(null, 1, 28m));

        var detail = SalesController.ToDetail(sale);

        Assert.Equal(0, detail.Items.Single().RefundedQuantity);
        Assert.Equal(28m, detail.RefundedTotal);
    }

    // ----------------------------------------------------------------- rounding

    [Theory]
    [InlineData("2.345", "2.35")]
    [InlineData("2.355", "2.36")]
    [InlineData("2.344", "2.34")]
    [InlineData("2.346", "2.35")]
    [InlineData("0.125", "0.13")]
    [InlineData("0.005", "0.01")]
    [InlineData("1.005", "1.01")]
    [InlineData("-2.345", "-2.35")]
    public void Round_UsesAwayFromZero(string input, string expected)
    {
        Assert.Equal(D(expected), SalesController.Round(D(input)));
    }

    [Fact]
    public void Round_DiffersFromBankersRounding_OnMidpoints()
    {
        Assert.Equal(2.34m, Math.Round(2.345m, 2, MidpointRounding.ToEven));
        Assert.Equal(2.35m, SalesController.Round(2.345m));
    }

    [Fact]
    public void CalculateRefundAmount_RoundsMidpointAwayFromZero()
    {
        // 1.00 / 8 units = 0.125 per unit -> must round up to 0.13 (banker's rounding would give 0.12)
        var sale = MakeSale(1.00m, 0m, 0m, Item(1, 8, 1.00m));

        var amount = SalesController.CalculateRefundAmount(sale, sale.Items.Single(), 1);

        Assert.Equal(0.13m, amount);
    }

    // --------------------------------------------------- refund amount calculation

    [Fact]
    public void CalculateRefundAmount_IncludesVat()
    {
        // 100 + 12% VAT = 112 for 4 units -> 28 per unit
        var sale = MakeSale(100m, 0m, 12m, Item(1, 4, 100m));
        var item = sale.Items.Single();

        Assert.Equal(28.00m, SalesController.CalculateRefundAmount(sale, item, 1));
        Assert.Equal(112.00m, SalesController.CalculateRefundAmount(sale, item, 4));
    }

    [Fact]
    public void CalculateRefundAmount_SharesTheDiscountAndVatAcrossUnits()
    {
        // subtotal 400, discount 40, VAT on 360 = 43.20, customer paid 403.20 (ratio 1.008)
        var sale = MakeSale(400m, 40m, 43.20m, Item(1, 4, 400m));
        var item = sale.Items.Single();

        Assert.Equal(403.20m, sale.Total);
        Assert.Equal(100.80m, SalesController.CalculateRefundAmount(sale, item, 1));
        Assert.Equal(201.60m, SalesController.CalculateRefundAmount(sale, item, 2));
        Assert.Equal(403.20m, SalesController.CalculateRefundAmount(sale, item, 4));
    }

    [Fact]
    public void CalculateRefundAmount_DistributesAcrossMultipleLines()
    {
        // A: 3 units for 100, B: 2 units for 200, VAT 12% -> total 336
        var sale = MakeSale(300m, 0m, 36m, Item(1, 3, 100m), Item(2, 2, 200m));
        var a = sale.Items.Single(i => i.Id == 1);
        var b = sale.Items.Single(i => i.Id == 2);

        Assert.Equal(37.33m, SalesController.CalculateRefundAmount(sale, a, 1));   // 100/3*1.12 = 37.3333
        Assert.Equal(112.00m, SalesController.CalculateRefundAmount(sale, b, 1));  // 200/2*1.12
        Assert.Equal(112.00m, SalesController.CalculateRefundAmount(sale, a, 3));
        Assert.Equal(224.00m, SalesController.CalculateRefundAmount(sale, b, 2));
    }

    // ---------------------------------------------------------- final refund settle

    [Fact]
    public void SettleFinalRefund_AddsTheMissingCentavoToTheLastLine()
    {
        // total 112, 37.33 already refunded, 2 units left calculated as 2 x 37.33 = 74.66
        var lines = new List<RefundItem> { RefundLine(1, 1, 37.33m), RefundLine(1, 1, 37.33m) };

        var amount = SalesController.SettleFinalRefund(lines, 112m, 37.33m);

        Assert.Equal(74.67m, amount);
        Assert.Equal(37.34m, lines[^1].Amount);
        Assert.Equal(37.33m, lines[0].Amount);
    }

    [Fact]
    public void SettleFinalRefund_RemovesAnOverpaidCentavo()
    {
        var lines = new List<RefundItem> { RefundLine(1, 1, 0.13m), RefundLine(1, 1, 0.13m) };

        var amount = SalesController.SettleFinalRefund(lines, 0.25m, 0m);

        Assert.Equal(0.25m, amount);
        Assert.Equal(0.12m, lines[^1].Amount);
    }

    [Fact]
    public void SettleFinalRefund_LeavesAmountsAloneWhenAlreadyExact()
    {
        var lines = new List<RefundItem> { RefundLine(1, 4, 403.20m) };

        var amount = SalesController.SettleFinalRefund(lines, 403.20m, 0m);

        Assert.Equal(403.20m, amount);
        Assert.Equal(403.20m, lines[0].Amount);
    }

    [Fact]
    public void FinalRefund_SettlesExactlyToTheRemainingSaleTotal_AcrossSeveralRefunds()
    {
        // 3 units for 100 + 12% VAT = 112. Per-unit refunds round to 37.33, so three of them would leave a centavo.
        var sale = MakeSale(100m, 0m, 12m, Item(1, 3, 100m));
        var item = sale.Items.Single();

        var amounts = new List<decimal>();
        var refunded = 0m;
        for (var n = 1; n <= 3; n++)
        {
            var lines = new List<RefundItem>
            {
                RefundLine(1, 1, SalesController.CalculateRefundAmount(sale, item, 1)),
            };
            var isFinal = n == 3;
            var amount = isFinal
                ? SalesController.SettleFinalRefund(lines, sale.Total, refunded)
                : lines.Sum(l => l.Amount);

            amounts.Add(amount);
            refunded += amount;
        }

        Assert.Equal(new[] { 37.33m, 37.33m, 37.34m }, amounts.ToArray());
        Assert.Equal(sale.Total, refunded);
    }
}