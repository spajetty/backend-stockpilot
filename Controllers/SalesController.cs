using System.Globalization;
using System.Security.Claims;
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Pos;
using backend_stockpilot.Dtos.Sales;
using backend_stockpilot.Hubs;
using backend_stockpilot.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace backend_stockpilot.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize(Roles = "Owner,Manager,Cashier")]
public class SalesController(AppDbContext db, IHubContext<StockHub> hub, ILogger<SalesController> log) : ControllerBase
{
    private string? Claim(string type) => User.FindFirst(type)?.Value;

    internal static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static string AuditSummary(string head, string reason)
    {
        const int max = 500;
        var prefix = $"{head}. Reason: ";
        var room = max - prefix.Length;
        return reason.Length <= room ? prefix + reason : prefix + reason[..(room - 1)] + "…";
    }

    internal static decimal CalculateRefundAmount(Sale sale, SaleItem item, int quantity)
    {
        var ratio = sale.Subtotal > 0 ? sale.Total / sale.Subtotal : 1m;
        return Round(item.LineTotal * quantity / item.Quantity * ratio);
    }

    internal static decimal SettleFinalRefund(List<RefundItem> items, decimal saleTotal, decimal alreadyRefunded)
    {
        var amount = items.Sum(i => i.Amount);
        var diff = (saleTotal - alreadyRefunded) - amount;
        items[^1].Amount += diff;
        return amount + diff;
    }

    // Cashier -> only their own sales. Manager -> their store.
    // Owner -> no extra filter here; the global store filter handles the selected store.
    private IQueryable<Sale> ScopedSales(bool track = false)
    {
        IQueryable<Sale> q = track ? db.Sales : db.Sales.AsNoTracking();
        if (User.IsInRole("Owner")) return q;

        if (!int.TryParse(Claim("store_id"), out var sid)) return q.Where(_ => false);
        q = q.Where(s => s.StoreId == sid);

        if (User.IsInRole("Cashier"))
        {
            if (!int.TryParse(Claim("sub") ?? Claim(ClaimTypes.NameIdentifier), out var uid))
                return q.Where(_ => false);
            q = q.Where(s => s.CashierId == uid);
        }
        return q;
    }

    internal static string DisplayStatus(SaleStatus status, SalePaymentStatus pay) =>
        status == SaleStatus.Voided ? "Voided"
        : pay == SalePaymentStatus.Refunded ? "Refunded"
        : pay == SalePaymentStatus.PartiallyRefunded ? "PartiallyRefunded"
        : "Completed";

    internal static SaleDetailDto ToDetail(Sale sale)
    {
        var refundedQty = sale.Refunds
            .SelectMany(r => r.Items)
            .Where(ri => ri.SaleItemId != null)
            .GroupBy(ri => ri.SaleItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(ri => ri.Quantity));

        return new SaleDetailDto(
            sale.Id, sale.SaleNumber, sale.CreatedAt, sale.CashierName,
            sale.PaymentMethod.ToString(), sale.PaymentStatus.ToString(),
            DisplayStatus(sale.Status, sale.PaymentStatus),
            sale.Subtotal, sale.Discount, sale.Tax, sale.Total,
            sale.Refunds.Sum(r => r.Amount),
            sale.Items.OrderBy(i => i.Id)
                .Select(i => new SaleDetailItemDto(
                    i.Id, i.ProductName, i.Sku, i.Quantity,
                    refundedQty.GetValueOrDefault(i.Id), i.UnitPrice, i.LineTotal)).ToList(),
            sale.Refunds.OrderByDescending(r => r.CreatedAt)
                .Select(r => new SaleRefundDto(r.Id, r.Amount, r.Reason, r.ProcessedName, r.CreatedAt)).ToList());
    }

    [HttpGet]
    public async Task<ActionResult<SalesPageDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 5,
        [FromQuery] string sortBy = "date",
        [FromQuery] string sortDir = "desc",
        [FromQuery] string? payment = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        var q = ScopedSales();

        if (!string.IsNullOrWhiteSpace(payment) && !payment.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<PaymentMethod>(payment, true, out var method))
                return BadRequest(new { message = "Unknown payment method." });
            q = q.Where(s => s.PaymentMethod == method);
        }

        switch ((status ?? "").ToLowerInvariant())
        {
            case "":
            case "all":
                break;
            case "completed":
                q = q.Where(s => s.Status != SaleStatus.Voided && s.PaymentStatus == SalePaymentStatus.Paid);
                break;
            case "partiallyrefunded":
                q = q.Where(s => s.Status != SaleStatus.Voided && s.PaymentStatus == SalePaymentStatus.PartiallyRefunded);
                break;
            case "refunded":
                q = q.Where(s => s.Status != SaleStatus.Voided && s.PaymentStatus == SalePaymentStatus.Refunded);
                break;
            case "voided":
                q = q.Where(s => s.Status == SaleStatus.Voided);
                break;
            default:
                return BadRequest(new { message = "Unknown status." });
        }

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

            var methods = Enum.GetValues<PaymentMethod>()
                .Where(m => m.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var hasAmount = decimal.TryParse(
                term.Replace("₱", "").Replace(",", ""),
                NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);

            q = q.Where(s =>
                EF.Functions.ILike(s.SaleNumber, pattern) ||
                EF.Functions.ILike(s.CashierName, pattern) ||
                methods.Contains(s.PaymentMethod) ||
                (hasAmount && (s.Total == amount || s.Subtotal == amount)) ||
                s.Items.Any(i => EF.Functions.ILike(i.ProductName, pattern) ||
                                 EF.Functions.ILike(i.Sku, pattern)));
        }

        var asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<Sale> ordered = (sortBy.ToLowerInvariant(), asc) switch
        {
            ("total", true) => q.OrderBy(s => s.Total).ThenByDescending(s => s.CreatedAt),
            ("total", false) => q.OrderByDescending(s => s.Total).ThenByDescending(s => s.CreatedAt),
            (_, true) => q.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id),
            _ => q.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id),
        };

        var total = await q.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.SaleNumber,
                s.CreatedAt,
                s.CashierName,
                s.Subtotal,
                s.Total,
                s.PaymentMethod,
                s.PaymentStatus,
                s.Status,
                Units = s.Items.Sum(i => i.Quantity),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new SaleListItemDto(
            r.Id, r.SaleNumber, r.CreatedAt, r.CashierName, r.Units, r.Subtotal, r.Total,
            r.PaymentMethod.ToString(), r.PaymentStatus.ToString(), DisplayStatus(r.Status, r.PaymentStatus))).ToList();

        return new SalesPageDto(items, page, pageSize, total, totalPages);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SaleDetailDto>> Get(int id, CancellationToken ct)
    {
        var sale = await ScopedSales()
            .Include(s => s.Items)
            .Include(s => s.Refunds).ThenInclude(r => r.Items)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (sale is null) return NotFound();
        return ToDetail(sale);
    }

    // Manager and Owner only (the class allows Cashier, this narrows it)
    [HttpPost("{id:int}/refunds")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<SaleDetailDto>> CreateRefund(int id, [FromBody] RefundRequest req, CancellationToken ct)
    {
        var reason = req.Reason?.Trim() ?? "";
        if (reason.Length == 0) return BadRequest(new { message = "A reason is required." });
        if (reason.Length > 500) return BadRequest(new { message = "Reason is too long (500 characters max)." });
        if (req.Items is null || req.Items.Count == 0 || req.Items.Any(i => i.Quantity <= 0))
            return BadRequest(new { message = "Pick at least one item to refund." });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // One refund at a time per sale, so two managers can't refund the same items twice
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(2, {id})", ct);

        var sale = await ScopedSales(track: true)
            .Include(s => s.Items)
            .Include(s => s.Refunds).ThenInclude(r => r.Items)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (sale is null) return NotFound();
        if (sale.Status == SaleStatus.Voided)
            return BadRequest(new { message = "A voided sale can't be refunded." });

        var alreadyRefunded = sale.Refunds.Sum(r => r.Amount);
        if (alreadyRefunded >= sale.Total)
            return BadRequest(new { message = "This sale is already fully refunded." });

        var refundedQty = sale.Refunds
            .SelectMany(r => r.Items)
            .Where(ri => ri.SaleItemId != null)
            .GroupBy(ri => ri.SaleItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(ri => ri.Quantity));

        var wanted = req.Items.GroupBy(i => i.SaleItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var refundItems = new List<RefundItem>();

        foreach (var (saleItemId, qty) in wanted)
        {
            var item = sale.Items.FirstOrDefault(i => i.Id == saleItemId);
            if (item is null)
                return BadRequest(new { message = "One of the items isn't part of this sale." });

            var remaining = item.Quantity - refundedQty.GetValueOrDefault(saleItemId);
            if (qty > remaining)
                return BadRequest(new { message = $"Only {remaining} of {item.ProductName} can still be refunded." });

            refundItems.Add(new RefundItem
            {
                SaleItemId = saleItemId,
                ProductId = item.ProductId,
                Quantity = qty,
                Amount = CalculateRefundAmount(sale, item, qty),
            });
        }

        var amount = refundItems.Sum(r => r.Amount);

        // Last refund on the sale: settle to the exact total so rounding never leaves a centavo behind
        var fullyRefunded = sale.Items.All(i =>
            refundedQty.GetValueOrDefault(i.Id) + wanted.GetValueOrDefault(i.Id) >= i.Quantity);
        if (fullyRefunded)
            amount = SettleFinalRefund(refundItems, sale.Total, alreadyRefunded);

        if (alreadyRefunded + amount > sale.Total)
            return BadRequest(new { message = "That's more than what's left to refund on this sale." });

        var userId = int.TryParse(Claim("sub") ?? Claim(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var actorName = Claim("name") ?? Claim(ClaimTypes.Name) ?? "Staff";
        var role = Enum.TryParse<UserRole>(Claim("role") ?? Claim(ClaimTypes.Role), out var parsedRole) ? parsedRole : UserRole.Manager;
        var now = DateTime.UtcNow;

        var refund = new Refund
        {
            StoreId = sale.StoreId,
            SaleId = sale.Id,
            Amount = amount,
            Reason = reason,
            ProcessedById = userId,
            ProcessedName = actorName,
            CreatedAt = now,
        };
        foreach (var ri in refundItems) refund.Items.Add(ri);
        sale.Refunds.Add(refund);

        sale.PaymentStatus = alreadyRefunded + amount >= sale.Total
            ? SalePaymentStatus.Refunded
            : SalePaymentStatus.PartiallyRefunded;

        var changes = new List<StockChange>();
        if (req.ReturnToStock)
        {
            foreach (var ri in refundItems.Where(x => x.ProductId != null))
            {
                var pid = ri.ProductId!.Value;
                var qty = ri.Quantity;
                var line = sale.Items.First(i => i.Id == ri.SaleItemId);

                var updated = await db.InventoryLevels
                    .Where(i => i.StoreId == sale.StoreId && i.ProductId == pid)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.QuantityOnHand, i => i.QuantityOnHand + qty)
                        .SetProperty(i => i.UpdatedAt, now), ct);

                int after;
                if (updated == 0)
                {
                    db.InventoryLevels.Add(new InventoryLevel
                    {
                        StoreId = sale.StoreId,
                        ProductId = pid,
                        QuantityOnHand = qty,
                        UpdatedAt = now,
                    });
                    after = qty;
                }
                else
                {
                    after = await db.InventoryLevels.AsNoTracking()
                        .Where(i => i.StoreId == sale.StoreId && i.ProductId == pid)
                        .Select(i => i.QuantityOnHand).SingleAsync(ct);
                }

                changes.Add(new StockChange(pid, after));

                db.StockMovements.Add(new StockMovement
                {
                    StoreId = sale.StoreId,
                    ProductId = pid,
                    MovementType = MovementType.Refund,
                    QuantityChange = qty,
                    QuantityBefore = after - qty,
                    QuantityAfter = after,
                    UnitCost = line.UnitCost,
                    Reason = reason,
                    Reference = sale.SaleNumber,
                    Approval = ApprovalStatus.NotRequired,
                    CreatedById = userId,
                    CreatedAt = now,
                });
            }
        }

        db.AuditLogs.Add(new AuditLog
        {
            StoreId = sale.StoreId,
            ActorId = userId,
            ActorName = actorName,
            ActorRole = role,
            Action = "Sale refunded",
            Entity = "Sale",
            EntityId = sale.SaleNumber,
            Summary = AuditSummary(
                $"{sale.SaleNumber}: refunded ₱{amount:N2} ({refundItems.Sum(x => x.Quantity)} units)"
                + (req.ReturnToStock ? ", returned to stock" : ""), reason),
            Outcome = AuditOutcome.Success,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (changes.Count > 0)
        {
            try { await hub.Clients.Group(StockHub.Group(sale.StoreId)).SendAsync("stockChanged", changes, ct); }
            catch (Exception ex) { log.LogWarning(ex, "stockChanged broadcast failed for store {StoreId}", sale.StoreId); }
        }

        return ToDetail(sale);
    }
}