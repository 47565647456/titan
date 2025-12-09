using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

/// <summary>
/// SignalR hub for real-time item type registry updates.
/// Designers can subscribe to receive notifications when item types are created, updated, or deleted.
/// </summary>
public class ItemTypeHub : Hub
{
    /// <summary>
    /// Join the item-types group to receive updates.
    /// </summary>
    public async Task JoinItemTypesGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "item-types");
    }

    /// <summary>
    /// Leave the item-types group.
    /// </summary>
    public async Task LeaveItemTypesGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "item-types");
    }

    /// <summary>
    /// Broadcast item type created event.
    /// </summary>
    public static async Task NotifyItemTypeCreated(IHubContext<ItemTypeHub> hubContext, ItemTypeDefinition definition)
    {
        await hubContext.Clients.Group("item-types").SendAsync("ItemTypeCreated", definition);
    }

    /// <summary>
    /// Broadcast item type updated event.
    /// </summary>
    public static async Task NotifyItemTypeUpdated(IHubContext<ItemTypeHub> hubContext, ItemTypeDefinition definition)
    {
        await hubContext.Clients.Group("item-types").SendAsync("ItemTypeUpdated", definition);
    }

    /// <summary>
    /// Broadcast item type deleted event.
    /// </summary>
    public static async Task NotifyItemTypeDeleted(IHubContext<ItemTypeHub> hubContext, string itemTypeId)
    {
        await hubContext.Clients.Group("item-types").SendAsync("ItemTypeDeleted", itemTypeId);
    }
}
