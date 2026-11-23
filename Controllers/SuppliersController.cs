using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Products;
using backend_stockpilot.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend_stockpilot.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize(Roles = "Owner,Manager,InventoryClerk")]
public class SuppliersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<LookupOptionDto>> List(CancellationToken ct) =>
        await db.Suppliers.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new LookupOptionDto(s.Id, s.Name))
            .ToListAsync(ct);

    // Quick-add from the product form: name only, the rest can be filled in later
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<LookupOptionDto>> Create([FromBody] CreateLookupRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length == 0) return BadRequest(new { message = "Supplier name is required." });
        if (name.Length > 150) return BadRequest(new { message = "Supplier name is too long (150 characters max)." });

        var lower = name.ToLower();
        var existing = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsActive && s.Name.ToLower() == lower, ct);
        if (existing is not null) return new LookupOptionDto(existing.Id, existing.Name);

        var now = DateTime.UtcNow;
        var supplier = new Supplier { Name = name, IsActive = true, CreatedAt = now, UpdatedAt = now };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);

        return new LookupOptionDto(supplier.Id, supplier.Name);
    }
}