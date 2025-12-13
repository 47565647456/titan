using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for InventoryHub operations.
/// Used with TypedSignalR.Client source generator.
/// </summary>
public interface IInventoryHubClient
{
    /// <summary>
    /// Get all items for a character in a season (verifies ownership).
    /// </summary>
    Task<IReadOnlyList<Item>> GetInventory(Guid characterId, string seasonId);

    /// <summary>
    /// Add a new item to a character's inventory (verifies ownership).
    /// </summary>
    Task<Item> AddItem(Guid characterId, string seasonId, string itemTypeId, int quantity, Dictionary<string, string>? metadata);

    /// <summary>
    /// Get item history. Available to all authenticated users for provenance verification.
    /// </summary>
    Task<IReadOnlyList<ItemHistoryEntry>> GetItemHistory(Guid itemId);

    /// <summary>
    /// Get a specific item by ID (verifies ownership).
    /// </summary>
    Task<Item?> GetItem(Guid characterId, string seasonId, Guid itemId);

    /// <summary>
    /// Remove an item from inventory (verifies ownership).
    /// </summary>
    Task<bool> RemoveItem(Guid characterId, string seasonId, Guid itemId);

    /// <summary>
    /// Check if character has a specific item (verifies ownership).
    /// </summary>
    Task<bool> HasItem(Guid characterId, string seasonId, Guid itemId);
}
