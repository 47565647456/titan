using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for inventory operations.
/// All operations verify the character belongs to the authenticated user.
/// </summary>
[Authorize]
public class InventoryHub : Hub
{
    private readonly IClusterClient _clusterClient;

    public InventoryHub(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Gets the authenticated user's ID from the JWT token.
    /// </summary>
    private Guid GetUserId() => Guid.Parse(Context.UserIdentifier!);

    /// <summary>
    /// Verifies that the specified character belongs to the authenticated user.
    /// </summary>
    private async Task VerifyCharacterOwnershipAsync(Guid characterId)
    {
        var accountGrain = _clusterClient.GetGrain<IAccountGrain>(GetUserId());
        var characters = await accountGrain.GetCharactersAsync();
        
        if (!characters.Any(c => c.CharacterId == characterId))
        {
            throw new HubException("Character does not belong to this account.");
        }
    }

    /// <summary>
    /// Get all items for a character in a season (verifies ownership).
    /// </summary>
    public async Task<IReadOnlyList<Item>> GetInventory(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.GetItemsAsync();
    }

    /// <summary>
    /// Add a new item to a character's inventory (verifies ownership).
    /// </summary>
    public async Task<Item> AddItem(Guid characterId, string seasonId, string itemTypeId, int quantity = 1, Dictionary<string, string>? metadata = null)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.AddItemAsync(itemTypeId, quantity, metadata);
    }

    /// <summary>
    /// Get item history. Available to all authenticated users for provenance verification.
    /// </summary>
    public async Task<IReadOnlyList<ItemHistoryEntry>> GetItemHistory(Guid itemId)
    {
        var grain = _clusterClient.GetGrain<IItemHistoryGrain>(itemId);
        return await grain.GetHistoryAsync();
    }

    /// <summary>
    /// Get a specific item by ID (verifies ownership).
    /// </summary>
    public async Task<Item?> GetItem(Guid characterId, string seasonId, Guid itemId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.GetItemAsync(itemId);
    }

    /// <summary>
    /// Remove an item from inventory (verifies ownership).
    /// </summary>
    public async Task<bool> RemoveItem(Guid characterId, string seasonId, Guid itemId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.RemoveItemAsync(itemId);
    }

    /// <summary>
    /// Check if character has a specific item (verifies ownership).
    /// </summary>
    public async Task<bool> HasItem(Guid characterId, string seasonId, Guid itemId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<IInventoryGrain>(characterId, seasonId);
        return await grain.HasItemAsync(itemId);
    }
}

