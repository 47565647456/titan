using Orleans;
using Orleans.Transactions.Abstractions;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing a character's inventory within a season.
/// Key: (CharacterId, SeasonId)
/// Supports Orleans transactions for atomic multi-grain operations.
/// </summary>
public interface IInventoryGrain : IGrainWithGuidCompoundKey
{
    /// <summary>
    /// Get all items in the inventory.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<List<Item>> GetItemsAsync();

    /// <summary>
    /// Get a specific item by ID.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<Item?> GetItemAsync(Guid itemId);

    /// <summary>
    /// Add a new item to the inventory.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<Item> AddItemAsync(string itemTypeId, int quantity = 1, Dictionary<string, object>? metadata = null);

    /// <summary>
    /// Remove an item from the inventory.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<bool> RemoveItemAsync(Guid itemId);

    /// <summary>
    /// Check if an item exists in the inventory.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<bool> HasItemAsync(Guid itemId);

    /// <summary>
    /// Transfer an item out of this inventory (for trading).
    /// Returns the item if successful, null if item doesn't exist.
    /// </summary>
    [Transaction(TransactionOption.Join)]
    Task<Item?> TransferItemOutAsync(Guid itemId);

    /// <summary>
    /// Transfer an item into this inventory (for trading).
    /// </summary>
    [Transaction(TransactionOption.Join)]
    Task TransferItemInAsync(Item item);
}


