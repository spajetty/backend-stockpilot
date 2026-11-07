using System.Security.Claims;
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Pos;
using backend_stockpilot.Hubs;
using backend_stockpilot.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace backend_stockpilot.Controllers;

[ApiController]
[Route("api/pos")]
[Authorize(Roles = "Owner,Manager,Cashier")]
public class PosController(AppDbContext db, IHubContext<StockHub> hub, ILogger<PosController> log) : ControllerBase
{
    private string? Claim(string type) => User.FindFirst(type)?.Value;

    private bool TryResolveStore(int? requested, out int storeId)
    {
        storeId = 0;
        if (User.IsInRole("Owner"))
        {
            if (requested is null) return false;
            storeId = requested.Value;
            return true;
        }
        return int.TryParse(Claim("store_id"), out storeId);
    }

    private ActionResult StoreError() =>
        User.IsInRole("Owner") ? BadRequest(new { message = "storeId is required for the owner." }) : Forbid();

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static TimeZoneInfo ResolveTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }

    [HttpGet("catalog")]
    public async Task<ActionResult<PosCatalogDto>> GetCatalog([FromQuery] int? storeId, CancellationToken ct)
    {
        if (!TryResolveStore(storeId, out var sid)) return StoreError();

        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct);
        if (store is null) return NotFound();

        var categories = await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new PosCategoryDto(c.Id, c.Name))
            .ToListAsync(ct);

        var products = await (
            from p in db.Products.AsNoTracking()
            where p.IsActive && p.ArchivedAt == null
            join i in db.InventoryLevels.AsNoTracking().Where(i => i.StoreId == sid)
                on p.Id equals i.ProductId into inv
            from i in inv.DefaultIfEmpty()
            orderby p.Name
            select new PosProductDto(
                p.Id, p.Name, p.Sku, p.Barcode, p.CategoryId, p.SellingPrice, p.ImageUrl,
                i == null ? 0 : i.QuantityOnHand,
                i == null ? p.ReorderLevel : (i.ReorderLevelOverride ?? p.ReorderLevel))
        ).ToListAsync(ct);

        return new PosCatalogDto(sid, store.TaxRate, categories, products);
    }

    [HttpPost("checkout")]
    public async Task<ActionResult<CheckoutResponse>> Checkout([FromBody] CheckoutRequest req, CancellationToken ct)
    {
        if (!TryResolveStore(req.StoreId, out var sid)) return StoreError();

        if (req.Items is null || req.Items.Count == 0)
            return BadRequest(new { message = "The cart is empty." });
        if (req.Items.Any(i => i.Quantity <= 0 || i.Quantity > 999))
            return BadRequest(new { message = "Invalid quantity." });
        if (!Enum.TryParse<PaymentMethod>(req.PaymentMethod, true, out var method))
            return BadRequest(new { message = "Unknown payment method." });

        var wanted = req.Items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid && s.IsActive, ct);
        if (store is null) return NotFound(new { message = "Store not found." });

        var ids = wanted.Keys.ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.IsActive && p.ArchivedAt == null)
            .ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != ids.Count)
            return BadRequest(new { message = "One or more products are no longer available." });

        var lineTotals = wanted.ToDictionary(w => w.Key, w => Round(products[w.Key].SellingPrice * w.Value));
        var subtotal = lineTotals.Values.Sum();
        var discount = Round(req.Discount);
        if (discount < 0 || discount > subtotal)
            return BadRequest(new { message = "Discount can't be negative or more than the subtotal." });
        var tax = Round((subtotal - discount) * store.TaxRate / 100m);
        var total = subtotal - discount + tax;

        decimal? tendered = null, change = null;
        if (method == PaymentMethod.Cash)
        {
            if (req.CashTendered is null || Round(req.CashTendered.Value) < total)
                return BadRequest(new { message = "Cash tendered is less than the total." });
            tendered = Round(req.CashTendered.Value);
            change = tendered - total;
        }

        var userId = int.TryParse(Claim("sub") ?? Claim(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var cashierName = Claim("name") ?? Claim(ClaimTypes.Name) ?? "Cashier";
        var role = Enum.TryParse<UserRole>(Claim("role") ?? Claim(ClaimTypes.Role), out var r) ? r : UserRole.Cashier;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({sid})", ct);

        var nowUtc = DateTime.UtcNow;
        var tz = ResolveTz(store.TimeZone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, tz);
        var seq = await db.Sales.CountAsync(s => s.StoreId == sid && s.CreatedAt >= dayStartUtc, ct) + 1;
        var saleNumber = $"SP-{store.Code}-{localNow:yyMMdd}-{seq:0000}";

        var sale = new Sale
        {
            StoreId = sid,
            SaleNumber = saleNumber,
            CashierId = userId,
            CashierName = cashierName,
            Subtotal = subtotal,
            Discount = discount,
            Tax = tax,
            Total = total,
            PaymentMethod = method,
            PaymentStatus = SalePaymentStatus.Paid,
            Status = SaleStatus.Completed,
            CashTendered = tendered,
            ChangeDue = change,
            CreatedAt = nowUtc,
        };

        var changes = new List<StockChange>();
        foreach (var (pid, qty) in wanted)
        {
            var p = products[pid];

            var updated = await db.InventoryLevels
                .Where(i => i.StoreId == sid && i.ProductId == pid && i.QuantityOnHand >= qty)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.QuantityOnHand, i => i.QuantityOnHand - qty)
                    .SetProperty(i => i.UpdatedAt, nowUtc), ct);

            if (updated == 0)
                return Conflict(new { message = $"Not enough stock for {p.Name}.", productId = pid });

            var after = await db.InventoryLevels.AsNoTracking()
                .Where(i => i.StoreId == sid && i.ProductId == pid)
                .Select(i => i.QuantityOnHand).SingleAsync(ct);

            changes.Add(new StockChange(pid, after));

            sale.Items.Add(new SaleItem
            {
                ProductId = pid,
                ProductName = p.Name,
                Sku = p.Sku,
                Quantity = qty,
                UnitPrice = p.SellingPrice,
                UnitCost = p.CostPrice,
                LineTotal = lineTotals[pid],
            });

            db.StockMovements.Add(new StockMovement
            {
                StoreId = sid,
                ProductId = pid,
                MovementType = MovementType.Sale,
                QuantityChange = -qty,
                QuantityBefore = after + qty,
                QuantityAfter = after,
                UnitCost = p.CostPrice,
                Reference = saleNumber,
                Approval = ApprovalStatus.NotRequired,
                CreatedById = userId,
                CreatedAt = nowUtc,
            });
        }

        db.Sales.Add(sale);

        if (method != PaymentMethod.Cash)
            db.Payments.Add(new Payment
            {
                StoreId = sid,
                Sale = sale,
                Provider = method,
                Status = PaymentGatewayStatus.Captured,
                Amount = total,
                Currency = store.Currency,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            });

        db.AuditLogs.Add(new AuditLog
        {
            StoreId = sid,
            ActorId = userId,
            ActorName = cashierName,
            ActorRole = role,
            Action = "Sale completed",
            Entity = "Sale",
            EntityId = saleNumber,
            Summary = $"{saleNumber}: {wanted.Values.Sum()} units, ₱{total:N2} via {method}",
            Outcome = AuditOutcome.Success,
            CreatedAt = nowUtc,
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        try { await hub.Clients.Group(StockHub.Group(sid)).SendAsync("stockChanged", changes, ct); }
        catch (Exception ex) { log.LogWarning(ex, "stockChanged broadcast failed for store {StoreId}", sid); }

        var receipt = new ReceiptDto(
            sale.Id, saleNumber, nowUtc, store.Name, cashierName,
            sale.Items.Select(i => new ReceiptItemDto(i.ProductName, i.Sku, i.Quantity, i.UnitPrice, i.LineTotal)).ToList(),
            subtotal, discount, store.TaxRate, tax, total, method.ToString(), tendered, change);

        return Ok(new CheckoutResponse(receipt, changes));
    }
}