using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for inventory operations.
/// Replaces InventoryController with bidirectional communication.
/// </summary>
public class InventoryHub : Hub
{
    private readonly IClusterClient _clusterClient;

    public InventoryHub(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Get all items for a character in a season.
    /// </summary>
    public async Task<IReadOnlyList<Item>> GetInventory(Guid characterId, string seasonId)
    {
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.GetItemsAsync();
    }

    /// <summary>
    /// Add a new item to a character's inventory.
    /// </summary>
    public async Task<Item> AddItem(Guid characterId, string seasonId, string itemTypeId, int quantity = 1, Dictionary<string, object>? metadata = null)
    {
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.AddItemAsync(itemTypeId, quantity, metadata);
    }

    /// <summary>
    /// Get item history.
    /// </summary>
    public async Task<IReadOnlyList<ItemHistoryEntry>> GetItemHistory(Guid itemId)
    {
        var grain = _clusterClient.GetGrain<IItemHistoryGrain>(itemId);
        return await grain.GetHistoryAsync();
    }

    /// <summary>
    /// Get a specific item by ID.
    /// </summary>
    public async Task<Item?> GetItem(Guid characterId, string seasonId, Guid itemId)
    {
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.GetItemAsync(itemId);
    }

    /// <summary>
    /// Remove an item from inventory.
    /// </summary>
    public async Task<bool> RemoveItem(Guid characterId, string seasonId, Guid itemId)
    {
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.RemoveItemAsync(itemId);
    }

    /// <summary>
    /// Check if character has a specific item.
    /// </summary>
    public async Task<bool> HasItem(Guid characterId, string seasonId, Guid itemId)
    {
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.HasItemAsync(itemId);
    }
}
