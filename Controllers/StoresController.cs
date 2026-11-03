using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Stores;
using backend_stockpilot.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend_stockpilot.Controllers;

[ApiController]
[Route("api/stores")]
[Authorize]
public class StoresController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentStore _currentStore;

    public StoresController(AppDbContext db, ICurrentStore currentStore)
    {
        _db = db;
        _currentStore = currentStore;
    }

    // Owner: every active store (feeds the store switcher and the invite form).
    // Everyone else: only their own store (feeds the header label).
    [HttpGet]
    public async Task<ActionResult<List<StoreDto>>> GetAll(CancellationToken ct)
    {
        var query = _db.Stores.AsNoTracking().Where(s => s.IsActive);

        if (!_currentStore.IsOwner)
        {
            var ownStoreId = _currentStore.StoreId;
            query = query.Where(s => s.Id == ownStoreId);
        }

        var stores = await query
            .OrderBy(s => s.Name)
            .Select(s => new StoreDto(s.Id, s.Code, s.Name, s.Currency, s.TaxRate, s.TimeZone))
            .ToListAsync(ct);

        return Ok(stores);
    }
}