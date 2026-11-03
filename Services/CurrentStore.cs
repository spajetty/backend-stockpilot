using System.Security.Claims;
using backend_stockpilot.Models;
using backend_stockpilot.Services.Interfaces;

namespace backend_stockpilot.Services;

public class CurrentStore : ICurrentStore
{
    public const string StoreClaim = "store_id";
    public const string StoreHeader = "X-Store-Id";
    private const int NoStore = -1;

    private readonly IHttpContextAccessor _accessor;

    public CurrentStore(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;
    private bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int? UserId
    {
        get
        {
            var raw = Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? Principal?.FindFirst("sub")?.Value;
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    public bool IsOwner => IsAuthenticated && Principal!.IsInRole(nameof(UserRole.Owner));

    public int? StoreId
    {
        get
        {
            if (!IsAuthenticated) return null;

            if (IsOwner)
            {
                var header = _accessor.HttpContext!.Request.Headers[StoreHeader].ToString();
                return int.TryParse(header, out var selected) && selected > 0 ? selected : null;
            }

            var claim = Principal!.FindFirst(StoreClaim)?.Value;
            return int.TryParse(claim, out var own) ? own : NoStore;
        }
    }

    public bool TryGetWriteStoreId(out int storeId)
    {
        var current = StoreId;
        if (current is > 0)
        {
            storeId = current.Value;
            return true;
        }

        storeId = 0;
        return false;
    }
}