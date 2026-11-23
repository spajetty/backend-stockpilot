using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Products;
using backend_stockpilot.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend_stockpilot.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize(Roles = "Owner,Manager,InventoryClerk")]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<LookupOptionDto>> List(CancellationToken ct) =>
        await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new LookupOptionDto(c.Id, c.Name))
            .ToListAsync(ct);

    // Names are citext + unique, so asking for one that exists just returns it
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    public async Task<ActionResult<LookupOptionDto>> Create([FromBody] CreateLookupRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length == 0) return BadRequest(new { message = "Category name is required." });
        if (name.Length > 80) return BadRequest(new { message = "Category name is too long (80 characters max)." });

        var existing = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Name == name, ct);
        if (existing is not null) return new LookupOptionDto(existing.Id, existing.Name);

        var category = new Category { Name = name, CreatedAt = DateTime.UtcNow };
        db.Categories.Add(category);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(category).State = EntityState.Detached;
            var raced = await db.Categories.AsNoTracking().FirstAsync(c => c.Name == name, ct);
            return new LookupOptionDto(raced.Id, raced.Name);
        }

        return new LookupOptionDto(category.Id, category.Name);
    }
}