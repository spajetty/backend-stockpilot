using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace backend_stockpilot.Hubs;

[Authorize(Roles = "Owner,Manager,Cashier,InventoryClerk")]
public class StockHub : Hub
{
    public static string Group(int storeId) => $"store-{storeId}";

    public async Task JoinStore(int storeId)
    {
        var isOwner = Context.User!.IsInRole("Owner");
        var own = Context.User.FindFirst("store_id")?.Value;    
        if (!isOwner && own != storeId.ToString())
            throw new HubException("Not allowed for this store.");

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(storeId));
    }

    public Task LeaveStore(int storeId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(storeId));
}