using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Products;
using backend_stockpilot.Models;
using backend_stockpilot.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend_stockpilot.Controllers;

// Products are a shared catalogue (no StoreId). Stock comes from InventoryLevels, which the
// global store filter already scopes: manager = own store, owner = the store picked in the
// top bar (X-Store-Id) or every store when "All stores" is selected.
//
// Roles: Owner and Manager can add / edit / deactivate. InventoryClerk is view-only.
// A manager's new product is saved as Pending (inactive, hidden from the POS) until the owner approves it.
[ApiController]
[Route("api/products")]
[Authorize(Roles = "Owner,Manager,InventoryClerk")]
public class ProductsController(AppDbContext db, ICurrentStore currentStore) : ControllerBase
{
    private const int MaxQuantity = 1_000_000;
    private const decimal MaxPrice = 10_000_000m;
    private const int MaxExportRows = 10_000;

    private static readonly Expression<Func<Product, bool>> IsApproved =
        p => p.Approval == ApprovalStatus.NotRequired || p.Approval == ApprovalStatus.Approved;

    // ---------- request context ----------

    private string? Claim(string type) => User.FindFirst(type)?.Value;
    private bool IsOwner => User.IsInRole("Owner");
    private int? OwnStoreId => int.TryParse(Claim("store_id"), out var sid) ? sid : null;
    private string ActorName => Claim("name") ?? "Staff";
    private UserRole ActorRole => Enum.TryParse<UserRole>(Claim("role"), out var r) ? r : UserRole.Manager;
    private int? AuditStoreId => IsOwner ? (currentStore.StoreId is > 0 ? currentStore.StoreId : null) : OwnStoreId;

    // Owner sees everything. Everyone else sees approved products plus the pending/rejected
    // ones their own store submitted.
    private IQueryable<Product> VisibleProducts(bool track = false)
    {
        IQueryable<Product> q = track ? db.Products : db.Products.AsNoTracking();
        if (IsOwner) return q;
        if (OwnStoreId is not int sid) return q.Where(_ => false);

        return q.Where(p => p.Approval == ApprovalStatus.NotRequired
                         || p.Approval == ApprovalStatus.Approved
                         || p.RequestedByStoreId == sid);
    }

    // ---------- list / detail / export ----------

    private sealed record ProductRow(
        int Id, string Name, string Sku, string? Barcode, int? CategoryId, string? CategoryName,
        int? SupplierId, string? SupplierName, decimal CostPrice, decimal SellingPrice, int ReorderLevel,
        string? ImageUrl, bool IsActive, bool AvailableInPos, ApprovalStatus Approval,
        int? RequestedByStoreId, int PendingOpeningStock);

    private static IQueryable<ProductRow> Project(IQueryable<Product> q) =>
        q.Select(p => new ProductRow(
            p.Id, p.Name, p.Sku, p.Barcode, p.CategoryId,
            p.Category != null ? p.Category.Name : null,
            p.SupplierId,
            p.Supplier != null ? p.Supplier.Name : null,
            p.CostPrice, p.SellingPrice, p.ReorderLevel, p.ImageUrl, p.IsActive, p.AvailableInPos,
            p.Approval, p.RequestedByStoreId, p.PendingOpeningStock));

    private static string StatusOf(ApprovalStatus approval, bool isActive) => approval switch
    {
        ApprovalStatus.Pending => "Pending",
        ApprovalStatus.Rejected => "Rejected",
        _ => isActive ? "Active" : "Inactive",
    };

    private static decimal MarginOf(decimal cost, decimal price) =>
        price > 0 ? Math.Round((price - cost) / price * 100m, 1, MidpointRounding.AwayFromZero) : 0m;

    private (IQueryable<Product>? Query, string? Error) ApplyFilters(
        IQueryable<Product> q, string? search, int? categoryId, string? status)
    {
        switch ((status ?? "").ToLowerInvariant())
        {
            case "":
            case "all":
                // The owner reviews pending/rejected products from the approvals list, not the main table
                if (IsOwner) q = q.Where(IsApproved);
                break;
            case "active":
                q = q.Where(IsApproved).Where(p => p.IsActive);
                break;
            case "inactive":
                q = q.Where(IsApproved).Where(p => !p.IsActive);
                break;
            case "pending":
                q = q.Where(p => p.Approval == ApprovalStatus.Pending);
                break;
            case "rejected":
                q = q.Where(p => p.Approval == ApprovalStatus.Rejected);
                break;
            default:
                return (null, "Unknown status.");
        }

        if (categoryId is > 0) q = q.Where(p => p.CategoryId == categoryId);

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = q.Where(p =>
                EF.Functions.ILike(p.Name, pattern) ||
                EF.Functions.ILike(p.Sku, pattern) ||
                (p.Barcode != null && EF.Functions.ILike(p.Barcode, pattern)));
        }

        return (q, null);
    }

    private static IOrderedQueryable<Product> Order(IQueryable<Product> q, string? sort) =>
        (sort ?? "name_asc").ToLowerInvariant() switch
        {
            "name_desc" => q.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
            "price_asc" => q.OrderBy(p => p.SellingPrice).ThenBy(p => p.Name),
            "price_desc" => q.OrderByDescending(p => p.SellingPrice).ThenBy(p => p.Name),
            "newest" => q.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
            _ => q.OrderBy(p => p.Name).ThenBy(p => p.Id),
        };

    private async Task<List<ProductListItemDto>> ToItems(List<ProductRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Id).ToList();

        // The store query filter on InventoryLevels decides what this sums
        var levels = await db.InventoryLevels.AsNoTracking()
            .Where(i => ids.Contains(i.ProductId))
            .Select(i => new { i.ProductId, i.QuantityOnHand, i.ReorderLevelOverride })
            .ToListAsync(ct);

        var singleStore = currentStore.StoreId is > 0;
        var stock = levels
            .GroupBy(l => l.ProductId)
            .ToDictionary(
                g => g.Key,
                g => (Qty: g.Sum(x => x.QuantityOnHand),
                      Override: singleStore ? g.First().ReorderLevelOverride : null));

        var storeIds = rows.Where(r => r.RequestedByStoreId != null).Select(r => r.RequestedByStoreId!.Value).Distinct().ToList();
        var storeNames = storeIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return rows.Select(r =>
        {
            var hasStock = stock.TryGetValue(r.Id, out var st);
            var awaiting = r.Approval is ApprovalStatus.Pending or ApprovalStatus.Rejected;
            return new ProductListItemDto(
                r.Id, r.Name, r.Sku, r.Barcode, r.CategoryId, r.CategoryName, r.SupplierId, r.SupplierName,
                r.CostPrice, r.SellingPrice, MarginOf(r.CostPrice, r.SellingPrice),
                hasStock ? st.Qty : 0,
                (hasStock ? st.Override : null) ?? r.ReorderLevel,
                r.ImageUrl, StatusOf(r.Approval, r.IsActive), r.AvailableInPos,
                awaiting && r.RequestedByStoreId is int sid && storeNames.TryGetValue(sid, out var storeName) ? storeName : null,
                r.PendingOpeningStock);
        }).ToList();
    }

    [HttpGet]
    public async Task<ActionResult<ProductsPageDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 8,
        [FromQuery] string? search = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);

        var (q, error) = ApplyFilters(VisibleProducts(), search, categoryId, status);
        if (q is null) return BadRequest(new { message = error });

        var total = await q.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var rows = await Project(Order(q, sort).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return new ProductsPageDto(await ToItems(rows, ct), page, pageSize, total, totalPages);
    }

    private async Task<ProductDetailDto?> BuildDetail(int id, CancellationToken ct)
    {
        var row = await Project(VisibleProducts().Where(p => p.Id == id)).FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var extra = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.Description, p.RejectionReason })
            .FirstAsync(ct);

        var item = (await ToItems([row], ct))[0];

        var moves = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == id)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(5)
            .Select(m => new { m.Id, m.MovementType, m.Reference, m.Reason, m.QuantityChange, StoreCode = m.Store.Code, m.CreatedAt })
            .ToListAsync(ct);

        // Sale totals and cashier names aren't for inventory clerks
        var sales = User.IsInRole("InventoryClerk")
            ? new List<ProductSaleDto>()
            : await db.SaleItems.AsNoTracking()
                .Where(si => si.ProductId == id)
                .OrderByDescending(si => si.Sale.CreatedAt)
                .Take(5)
                .Select(si => new ProductSaleDto(si.Sale.Id, si.Sale.SaleNumber, si.Sale.CreatedAt, si.Sale.CashierName, si.Sale.Total))
                .ToListAsync(ct);

        return new ProductDetailDto(
            item.Id, item.Name, item.Sku, item.Barcode, item.CategoryId, item.CategoryName, item.SupplierId, item.SupplierName,
            item.CostPrice, item.SellingPrice, item.Margin, item.Stock, item.ReorderLevel, item.ImageUrl, item.Status,
            item.AvailableInPos, item.RequestedByStoreName, item.PendingOpeningStock,
            extra.Description, item.Stock * item.CostPrice, extra.RejectionReason,
            moves.Select(m => new ProductMovementDto(m.Id, m.MovementType.ToString(), m.Reference, m.Reason, m.QuantityChange, m.StoreCode, m.CreatedAt)).ToList(),
            sales);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDetailDto>> Get(int id, CancellationToken ct)
    {
        var detail = await BuildDetail(id, ct);
        if (detail is null) return NotFound();
        return detail;
    }

    // Same filters as the list, every matching row, as a CSV that opens cleanly in Excel
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? search = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        var (q, error) = ApplyFilters(VisibleProducts(), search, categoryId, status);
        if (q is null) return BadRequest(new { message = error });

        var rows = await Project(Order(q, sort).Take(MaxExportRows)).ToListAsync(ct);
        var items = await ToItems(rows, ct);

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("Name,SKU,Barcode,Category,Supplier,Cost price,Selling price,Margin %,Stock,Reorder level,Status");
        foreach (var i in items)
        {
            sb.AppendLine(string.Join(',',
                Csv(i.Name), Csv(i.Sku), Csv(i.Barcode), Csv(i.CategoryName), Csv(i.SupplierName),
                i.CostPrice.ToString("0.00", inv), i.SellingPrice.ToString("0.00", inv), i.Margin.ToString("0.0", inv),
                i.Stock.ToString(inv), i.ReorderLevel.ToString(inv), i.Status));
        }

        var bom = new UTF8Encoding(true).GetPreamble();
        var body = new UTF8Encoding(false).GetBytes(sb.ToString());
        return File(bom.Concat(body).ToArray(), "text/csv; charset=utf-8",
            $"stockpilot-products-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    // Quotes cells, and stops spreadsheet formulas from running ("=...", "+...", "@...")
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var s = value;
        if ("=+-@\t\r".Contains(s[0])) s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    // ---------- validation ----------

    private sealed record Fields(
        string Name, string Sku, string? Barcode, int? CategoryId, int? SupplierId,
        decimal Cost, decimal Price, int Reorder, string? ImageUrl, string? Description, bool AvailableInPos);

    private static string? Blank(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static Fields ToFields(
        string? name, string? sku, string? barcode, int? categoryId, int? supplierId,
        decimal cost, decimal price, int reorder, string? imageUrl, string? description, bool availableInPos) =>
        new(name?.Trim() ?? "", sku?.Trim() ?? "", Blank(barcode), categoryId is > 0 ? categoryId : null,
            supplierId is > 0 ? supplierId : null, Round(cost), Round(price), reorder,
            Blank(imageUrl), Blank(description), availableInPos);

    private static string? Validate(Fields f)
    {
        if (f.Name.Length == 0) return "Product name is required.";
        if (f.Name.Length > 200) return "Product name is too long (200 characters max).";
        if (f.Sku.Length == 0) return "SKU is required.";
        if (f.Sku.Length > 50) return "SKU is too long (50 characters max).";
        if (f.Sku.Any(char.IsWhiteSpace)) return "SKU can't contain spaces.";
        if (f.Barcode is { Length: > 64 }) return "Barcode is too long (64 characters max).";
        if (f.Cost < 0 || f.Cost > MaxPrice) return "Cost price must be between ₱0 and ₱10,000,000.";
        if (f.Price < 0 || f.Price > MaxPrice) return "Selling price must be between ₱0 and ₱10,000,000.";
        if (f.Reorder < 0 || f.Reorder > MaxQuantity) return "Reorder level must be between 0 and 1,000,000.";
        if (f.Description is { Length: > 1000 }) return "Description is too long (1,000 characters max).";
        if (f.ImageUrl is not null)
        {
            if (f.ImageUrl.Length > 500) return "Image URL is too long (500 characters max).";
            if (!Uri.TryCreate(f.ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return "Image URL must start with http:// or https://.";
        }
        return null;
    }

    private async Task<string?> CheckReferences(Fields f, CancellationToken ct)
    {
        if (f.CategoryId is int cid && !await db.Categories.AnyAsync(c => c.Id == cid, ct))
            return "That category doesn't exist.";
        if (f.SupplierId is int sid && !await db.Suppliers.AnyAsync(s => s.Id == sid && s.IsActive, ct))
            return "That supplier doesn't exist or is inactive.";
        return null;
    }

    private async Task<string?> CheckDuplicates(Fields f, int excludeId, CancellationToken ct)
    {
        // Sku is citext, so this is case-insensitive
        if (await db.Products.AnyAsync(p => p.Sku == f.Sku && p.Id != excludeId, ct))
            return $"SKU {f.Sku} is already used by another product.";
        if (f.Barcode is not null && await db.Products.AnyAsync(p => p.Barcode == f.Barcode && p.Id != excludeId, ct))
            return "That barcode is already used by another product.";
        return null;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private const string RaceMessage = "Another change just used that SKU or barcode. Check them and try again.";

    // ---------- audit ----------

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Money(decimal v) => "₱" + v.ToString("N2", CultureInfo.InvariantCulture);

    private AuditLog Audit(string action, string sku, string summary, int? storeId, string? before = null, string? after = null) => new()
    {
        StoreId = storeId,
        ActorId = currentStore.UserId,
        ActorName = ActorName,
        ActorRole = ActorRole,
        Action = action,
        Entity = "Product",
        EntityId = sku,
        Summary = Truncate(summary, 500),
        Outcome = AuditOutcome.Success,
        BeforeValue = before,
        AfterValue = after,
        CreatedAt = DateTime.UtcNow,
    };

    private sealed record Change(string Field, string? Before, string? After, bool HideValues = false);

    // ---------- create ----------

    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<ProductDetailDto>> Create([FromBody] ProductCreateRequest req, CancellationToken ct)
    {
        var f = ToFields(req.Name, req.Sku, req.Barcode, req.CategoryId, req.SupplierId, req.CostPrice,
            req.SellingPrice, req.ReorderLevel, req.ImageUrl, req.Description, req.AvailableInPos);

        if (Validate(f) is { } invalid) return BadRequest(new { message = invalid });
        if (req.OpeningStock < 0 || req.OpeningStock > MaxQuantity)
            return BadRequest(new { message = "Opening stock must be between 0 and 1,000,000." });
        if (await CheckReferences(f, ct) is { } badRef) return BadRequest(new { message = badRef });
        if (await CheckDuplicates(f, 0, ct) is { } dup) return Conflict(new { message = dup });

        // Which store the opening stock belongs to
        int? storeId;
        if (IsOwner)
        {
            storeId = req.StoreId is > 0 ? req.StoreId : (currentStore.StoreId is > 0 ? currentStore.StoreId : null);
            if (req.OpeningStock > 0 && storeId is null)
                return BadRequest(new { message = "Pick a store for the opening stock." });
            if (storeId is int chosen && !await db.Stores.AnyAsync(s => s.Id == chosen && s.IsActive, ct))
                return BadRequest(new { message = "That store doesn't exist." });
        }
        else
        {
            if (OwnStoreId is not int own) return Forbid();
            storeId = own;
        }

        var now = DateTime.UtcNow;
        var needsApproval = !IsOwner;

        var product = new Product
        {
            Name = f.Name,
            Sku = f.Sku,
            Barcode = f.Barcode,
            Description = f.Description,
            CategoryId = f.CategoryId,
            SupplierId = f.SupplierId,
            CostPrice = f.Cost,
            SellingPrice = f.Price,
            ReorderLevel = f.Reorder,
            ImageUrl = f.ImageUrl,
            AvailableInPos = f.AvailableInPos,
            IsActive = !needsApproval,
            Approval = needsApproval ? ApprovalStatus.Pending : ApprovalStatus.NotRequired,
            RequestedByStoreId = needsApproval ? storeId : null,
            PendingOpeningStock = needsApproval ? req.OpeningStock : 0,
            CreatedById = currentStore.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Products.Add(product);

        // Owner adds are live straight away, so the opening stock lands now
        if (!needsApproval && req.OpeningStock > 0 && storeId is int sid)
        {
            db.InventoryLevels.Add(new InventoryLevel
            {
                StoreId = sid,
                Product = product,
                QuantityOnHand = req.OpeningStock,
                UpdatedAt = now,
            });
            db.StockMovements.Add(new StockMovement
            {
                StoreId = sid,
                Product = product,
                MovementType = MovementType.Opening,
                QuantityChange = req.OpeningStock,
                QuantityBefore = 0,
                QuantityAfter = req.OpeningStock,
                UnitCost = f.Cost,
                Reason = "Opening stock",
                Approval = ApprovalStatus.NotRequired,
                CreatedById = currentStore.UserId,
                CreatedAt = now,
            });
        }

        db.AuditLogs.Add(needsApproval
            ? Audit("Product submitted", f.Sku,
                $"{f.Sku}: {f.Name} submitted for owner approval ({Money(f.Price)}, opening stock {req.OpeningStock})", storeId)
            : Audit("Product created", f.Sku,
                $"{f.Sku}: {f.Name} added ({Money(f.Price)}" + (req.OpeningStock > 0 ? $", opening stock {req.OpeningStock}" : "") + ")", AuditStoreId));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { message = RaceMessage });
        }

        var detail = await BuildDetail(product.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, detail);
    }

    // ---------- update ----------

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<ProductDetailDto>> Update(int id, [FromBody] ProductUpdateRequest req, CancellationToken ct)
    {
        var product = await VisibleProducts(track: true).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();

        var f = ToFields(req.Name, req.Sku, req.Barcode, req.CategoryId, req.SupplierId, req.CostPrice,
            req.SellingPrice, req.ReorderLevel, req.ImageUrl, req.Description, req.AvailableInPos);

        if (Validate(f) is { } invalid) return BadRequest(new { message = invalid });
        if (await CheckReferences(f, ct) is { } badRef) return BadRequest(new { message = badRef });
        if (await CheckDuplicates(f, id, ct) is { } dup) return Conflict(new { message = dup });

        var awaiting = product.Approval is (ApprovalStatus.Pending or ApprovalStatus.Rejected);
        int? newOpening = null;
        if (awaiting && req.PendingOpeningStock is int ps)
        {
            if (ps < 0 || ps > MaxQuantity)
                return BadRequest(new { message = "Opening stock must be between 0 and 1,000,000." });
            newOpening = ps;
        }

        var changes = new List<Change>();
        void Track(string field, string? before, string? after, bool hide = false)
        {
            if (before != after) changes.Add(new Change(field, before, after, hide));
        }
        async Task<string?> CategoryName(int? cid) =>
            cid is null ? null : await db.Categories.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct);
        async Task<string?> SupplierName(int? sid) =>
            sid is null ? null : await db.Suppliers.Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct);

        Track("name", product.Name, f.Name);
        Track("SKU", product.Sku, f.Sku);
        Track("barcode", product.Barcode, f.Barcode);
        if (product.CategoryId != f.CategoryId) Track("category", await CategoryName(product.CategoryId), await CategoryName(f.CategoryId));
        if (product.SupplierId != f.SupplierId) Track("supplier", await SupplierName(product.SupplierId), await SupplierName(f.SupplierId));
        if (product.CostPrice != f.Cost) Track("cost price", Money(product.CostPrice), Money(f.Cost));
        if (product.SellingPrice != f.Price) Track("selling price", Money(product.SellingPrice), Money(f.Price));
        Track("reorder level", product.ReorderLevel.ToString(CultureInfo.InvariantCulture), f.Reorder.ToString(CultureInfo.InvariantCulture));
        Track("image", product.ImageUrl, f.ImageUrl, hide: true);
        Track("description", product.Description, f.Description, hide: true);
        if (product.AvailableInPos != f.AvailableInPos)
            Track("POS availability", product.AvailableInPos ? "available" : "hidden", f.AvailableInPos ? "available" : "hidden");
        if (newOpening is int no && no != product.PendingOpeningStock)
            Track("opening stock", product.PendingOpeningStock.ToString(CultureInfo.InvariantCulture), no.ToString(CultureInfo.InvariantCulture));

        // A manager fixing a rejected product sends it back to the owner
        var resubmit = product.Approval == ApprovalStatus.Rejected && !IsOwner;

        if (changes.Count == 0 && !resubmit)
        {
            var unchanged = await BuildDetail(id, ct);
            return unchanged is null ? NotFound() : unchanged;
        }

        product.Name = f.Name;
        product.Sku = f.Sku;
        product.Barcode = f.Barcode;
        product.CategoryId = f.CategoryId;
        product.SupplierId = f.SupplierId;
        product.CostPrice = f.Cost;
        product.SellingPrice = f.Price;
        product.ReorderLevel = f.Reorder;
        product.ImageUrl = f.ImageUrl;
        product.Description = f.Description;
        product.AvailableInPos = f.AvailableInPos;
        if (newOpening is int opening) product.PendingOpeningStock = opening;
        product.UpdatedAt = DateTime.UtcNow;

        if (resubmit)
        {
            product.Approval = ApprovalStatus.Pending;
            product.RejectionReason = null;
            product.ReviewedById = null;
            product.ReviewedAt = null;
        }

        var parts = changes.Select(c => c.HideValues ? $"{c.Field} changed" : $"{c.Field} {c.Before ?? "none"} → {c.After ?? "none"}");
        var summary = $"{product.Sku}: " + (resubmit ? "resubmitted for approval. " : "") + string.Join("; ", parts);
        var before = JsonSerializer.Serialize(changes.Where(c => !c.HideValues).ToDictionary(c => c.Field, c => c.Before));
        var after = JsonSerializer.Serialize(changes.Where(c => !c.HideValues).ToDictionary(c => c.Field, c => c.After));

        db.AuditLogs.Add(Audit(resubmit ? "Product resubmitted" : "Product updated", product.Sku, summary,
            awaiting ? product.RequestedByStoreId : AuditStoreId, before, after));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { message = RaceMessage });
        }

        var detail = await BuildDetail(id, ct);
        return detail is null ? NotFound() : detail;
    }

    // ---------- deactivate / reactivate ----------
    // Both are catalogue-wide: an inactive product disappears from the POS in every store.
    // Stock and sales history stay untouched.

    [HttpPost("{id:int}/deactivate")]
    [Authorize(Roles = "Owner,Manager")]
    public Task<ActionResult<ProductDetailDto>> Deactivate(int id, CancellationToken ct) => SetActive(id, false, ct);

    [HttpPost("{id:int}/reactivate")]
    [Authorize(Roles = "Owner,Manager")]
    public Task<ActionResult<ProductDetailDto>> Reactivate(int id, CancellationToken ct) => SetActive(id, true, ct);

    private async Task<ActionResult<ProductDetailDto>> SetActive(int id, bool active, CancellationToken ct)
    {
        var product = await VisibleProducts(track: true).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();

        if (product.Approval is (ApprovalStatus.Pending or ApprovalStatus.Rejected))
            return BadRequest(new { message = "Only approved products can be deactivated or reactivated." });

        if (product.IsActive != active)
        {
            var now = DateTime.UtcNow;
            product.IsActive = active;
            product.ArchivedAt = active ? null : now;
            product.UpdatedAt = now;

            db.AuditLogs.Add(Audit(
                active ? "Product reactivated" : "Product deactivated", product.Sku,
                active ? $"{product.Sku}: reactivated, available again in the catalogue"
                       : $"{product.Sku}: deactivated, hidden from the POS in all stores",
                AuditStoreId));

            await db.SaveChangesAsync(ct);
        }

        var detail = await BuildDetail(id, ct);
        return detail is null ? NotFound() : detail;
    }

    // ---------- owner approval ----------

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Owner")]
    public async Task<ActionResult<ProductDetailDto>> Approve(int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // One review at a time per product, so a double click can't add the opening stock twice
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(3, {id})", ct);

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (product.Approval is not (ApprovalStatus.Pending or ApprovalStatus.Rejected))
            return BadRequest(new { message = "This product isn't waiting for approval." });

        var now = DateTime.UtcNow;
        var qty = product.PendingOpeningStock;
        var storeId = product.RequestedByStoreId;

        if (qty > 0 && storeId is int sid)
        {
            // IgnoreQueryFilters: the owner's selected store may differ from the requesting store
            var exists = await db.InventoryLevels.IgnoreQueryFilters()
                .AnyAsync(i => i.StoreId == sid && i.ProductId == id, ct);
            if (exists)
                return Conflict(new { message = "This product already has stock recorded in that store." });

            db.InventoryLevels.Add(new InventoryLevel { StoreId = sid, ProductId = id, QuantityOnHand = qty, UpdatedAt = now });
            db.StockMovements.Add(new StockMovement
            {
                StoreId = sid,
                ProductId = id,
                MovementType = MovementType.Opening,
                QuantityChange = qty,
                QuantityBefore = 0,
                QuantityAfter = qty,
                UnitCost = product.CostPrice,
                Reason = "Opening stock (approved by owner)",
                Approval = ApprovalStatus.NotRequired,
                CreatedById = currentStore.UserId,
                CreatedAt = now,
            });
        }

        product.Approval = ApprovalStatus.Approved;
        product.IsActive = true;
        product.ArchivedAt = null;
        product.PendingOpeningStock = 0;
        product.RejectionReason = null;
        product.ReviewedById = currentStore.UserId;
        product.ReviewedAt = now;
        product.UpdatedAt = now;

        db.AuditLogs.Add(Audit("Product approved", product.Sku,
            $"{product.Sku}: {product.Name} approved" + (qty > 0 ? $", {qty} units added as opening stock" : ""), storeId));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var detail = await BuildDetail(id, ct);
        return detail is null ? NotFound() : detail;
    }

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Owner")]
    public async Task<ActionResult<ProductDetailDto>> Reject(int id, [FromBody] RejectProductRequest req, CancellationToken ct)
    {
        var reason = req.Reason?.Trim() ?? "";
        if (reason.Length == 0) return BadRequest(new { message = "A reason is required." });
        if (reason.Length > 500) return BadRequest(new { message = "Reason is too long (500 characters max)." });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(3, {id})", ct);

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (product.Approval != ApprovalStatus.Pending)
            return BadRequest(new { message = "This product isn't waiting for approval." });

        var now = DateTime.UtcNow;
        product.Approval = ApprovalStatus.Rejected;
        product.IsActive = false;
        product.RejectionReason = reason;
        product.ReviewedById = currentStore.UserId;
        product.ReviewedAt = now;
        product.UpdatedAt = now;

        db.AuditLogs.Add(Audit("Product rejected", product.Sku,
            $"{product.Sku}: {product.Name} rejected. Reason: {reason}", product.RequestedByStoreId));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var detail = await BuildDetail(id, ct);
        return detail is null ? NotFound() : detail;
    }
}