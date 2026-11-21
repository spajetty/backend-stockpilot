using System.Net;
using backend_stockpilot.Data;
using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;

namespace StockPilot.IntegrationTests;

public partial class SalesIntegrationTests
{
    private async Task<List<StockMovement>> GetMovementsAsync(string saleNumber)
    {
        await using var db = NewDb();
        return await db.StockMovements.AsNoTracking()
            .Where(m => m.Reference == saleNumber)
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    private async Task<List<AuditLog>> GetAuditLogsAsync(string saleNumber)
    {
        await using var db = NewDb();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == saleNumber)
            .OrderBy(a => a.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task Inventory_ReturnsRefundedProductsToStockWhenRequested()
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        await SetStockAsync(s.Store.Id, product.Id, 10);
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, sale.Id, Req("Unopened", true, (ItemId(sale, 0), 3)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(13, await GetStockAsync(s.Store.Id, product.Id));
    }

    [Fact]
    public async Task Inventory_CreatesTheInventoryRowWhenTheProductHadNone()
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        Assert.Null(await GetStockAsync(s.Store.Id, product.Id));
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, sale.Id, Req("Unopened", true, (ItemId(sale, 0), 2)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, await GetStockAsync(s.Store.Id, product.Id));
        var movement = Assert.Single(await GetMovementsAsync(sale.SaleNumber));
        Assert.Equal(0, movement.QuantityBefore);
        Assert.Equal(2, movement.QuantityAfter);
    }

    [Fact]
    public async Task Inventory_DoesNotRestoreStockWhenReturnToStockIsFalse()
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        await SetStockAsync(s.Store.Id, product.Id, 10);
        using var client = ClientFor(s.Manager);

        var response = await PostRefundAsync(client, sale.Id, Req("Damaged, throw away", false, (ItemId(sale, 0), 3)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, await GetStockAsync(s.Store.Id, product.Id));
        Assert.Empty(await GetMovementsAsync(sale.SaleNumber));
    }

    [Fact]
    public async Task Inventory_CreatesAStockMovementForTheRefund()
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        await SetStockAsync(s.Store.Id, product.Id, 10);
        using var client = ClientFor(s.Manager);

        await PostRefundAsync(client, sale.Id, Req("Customer returned them", true, (ItemId(sale, 0), 3)));

        var movement = Assert.Single(await GetMovementsAsync(sale.SaleNumber));
        Assert.Equal(s.Store.Id, movement.StoreId);
        Assert.Equal(product.Id, movement.ProductId);
        Assert.Equal(MovementType.Refund, movement.MovementType);
        Assert.Equal(3, movement.QuantityChange);
        Assert.Equal(10, movement.QuantityBefore);
        Assert.Equal(13, movement.QuantityAfter);
        Assert.Equal(product.CostPrice, movement.UnitCost);
        Assert.Equal("Customer returned them", movement.Reason);
        Assert.Equal(sale.SaleNumber, movement.Reference);
        Assert.Equal(ApprovalStatus.NotRequired, movement.Approval);
        Assert.Equal(s.Manager.Id, movement.CreatedById);
    }

    [Fact]
    public async Task Inventory_CreatesOneMovementPerRefundedProduct_AndSkipsProductsThatWereNotRefunded()
    {
        var s = await NewScenarioAsync();
        var a = await CreateProductAsync();
        var b = await CreateProductAsync();
        var c = await CreateProductAsync();
        var sale = await CreateSaleAsync(s.Store, s.Cashier, [new Line(a, 2, 100m), new Line(b, 2, 100m), new Line(c, 2, 100m)]);
        await SetStockAsync(s.Store.Id, a.Id, 5);
        await SetStockAsync(s.Store.Id, b.Id, 7);
        await SetStockAsync(s.Store.Id, c.Id, 9);
        using var client = ClientFor(s.Manager);

        await PostRefundAsync(client, sale.Id, Req("Two of three", true, (ItemId(sale, 0), 1), (ItemId(sale, 1), 2)));

        var movements = await GetMovementsAsync(sale.SaleNumber);
        Assert.Equal(2, movements.Count);
        Assert.Equal(new[] { a.Id, b.Id }, movements.Select(m => m.ProductId).OrderBy(id => id).ToArray());
        Assert.Equal(6, await GetStockAsync(s.Store.Id, a.Id));
        Assert.Equal(9, await GetStockAsync(s.Store.Id, b.Id));
        Assert.Equal(9, await GetStockAsync(s.Store.Id, c.Id));
    }

    [Fact]
    public async Task Audit_RecordsTheManagerAsActorWithTheRefundDetails()
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        await SetStockAsync(s.Store.Id, product.Id, 10);
        using var client = ClientFor(s.Manager);

        await PostRefundAsync(client, sale.Id, Req("Wrong colour", true, (ItemId(sale, 0), 1)));

        var log = Assert.Single(await GetAuditLogsAsync(sale.SaleNumber));
        Assert.Equal(s.Store.Id, log.StoreId);
        Assert.Equal(s.Manager.Id, log.ActorId);
        Assert.Equal(s.Manager.Name, log.ActorName);
        Assert.Equal(UserRole.Manager, log.ActorRole);
        Assert.Equal("Sale refunded", log.Action);
        Assert.Equal("Sale", log.Entity);
        Assert.Equal(AuditOutcome.Success, log.Outcome);
        Assert.Contains(sale.SaleNumber, log.Summary);
        Assert.Contains($"₱{100.80m:N2}", log.Summary);
        Assert.Contains("1 units", log.Summary);
        Assert.Contains("returned to stock", log.Summary);
        Assert.Contains("Wrong colour", log.Summary);
    }

    [Fact]
    public async Task Audit_RecordsTheOwnerAsActorWhenTheOwnerRefunds()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var owner = ClientFor(s.Owner);

        var response = await PostRefundAsync(owner, sale.Id, Req("Owner decision", false, (ItemId(sale, 0), 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = Assert.Single(await GetAuditLogsAsync(sale.SaleNumber));
        Assert.Equal(s.Owner.Id, log.ActorId);
        Assert.Equal(s.Owner.Name, log.ActorName);
        Assert.Equal(UserRole.Owner, log.ActorRole);
        Assert.Equal(s.Store.Id, log.StoreId);
        Assert.DoesNotContain("returned to stock", log.Summary);
    }

    [Fact]
    public async Task Audit_IsNotWrittenForRejectedRefunds()
    {
        var (s, _, sale) = await NewDiscountedSaleAsync();
        using var client = ClientFor(s.Manager);

        await PostRefundAsync(client, sale.Id, Req("Too many", true, (ItemId(sale, 0), 99)));

        Assert.Empty(await GetAuditLogsAsync(sale.SaleNumber));
        Assert.Empty(await GetMovementsAsync(sale.SaleNumber));
    }

    private sealed class FailOnRefundAuditLogInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var writesRefundAudit = eventData.Context!.ChangeTracker.Entries<AuditLog>()
                .Any(e => e.State == EntityState.Added && e.Entity.Action == "Sale refunded");

            if (writesRefundAudit)
                throw new InvalidOperationException("Simulated failure while saving the refund.");

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Inventory_RollsBackEverythingWhenRefundProcessingFails()
    {
        var (s, product, sale) = await NewDiscountedSaleAsync();
        await SetStockAsync(s.Store.Id, product.Id, 10);

        await using var failing = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(new FailOnRefundAuditLogInterceptor()))));

        using var client = ClientFor(s.Manager, factory: failing);
        var response = await PostRefundAsync(client, sale.Id, Req("Will fail", true, (ItemId(sale, 0), 3)));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        Assert.Equal(10, await GetStockAsync(s.Store.Id, product.Id));
        Assert.Empty(await GetRefundsAsync(sale.Id));
        Assert.Empty(await GetMovementsAsync(sale.SaleNumber));
        Assert.Empty(await GetAuditLogsAsync(sale.SaleNumber));
        var reloaded = await ReloadSaleAsync(sale.Id);
        Assert.Equal(SalePaymentStatus.Paid, reloaded.PaymentStatus);
        Assert.Equal(SaleStatus.Completed, reloaded.Status);

        using var healthy = ClientFor(s.Manager);
        var retry = await PostRefundAsync(healthy, sale.Id, Req("Retry", true, (ItemId(sale, 0), 3)));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(13, await GetStockAsync(s.Store.Id, product.Id));
    }
}
