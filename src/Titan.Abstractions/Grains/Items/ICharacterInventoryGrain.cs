using Orleans;
using Orleans.Transactions.Abstractions;
using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Grains.Items;

/// <summary>
/// Per-character inventory grain with grid-based storage and equipment.
/// Key: (CharacterId, SeasonId)
/// </summary>
public interface ICharacterInventoryGrain : IGrainWithGuidCompoundKey
{
    #region Stats

    /// <summary>
    /// Gets the character's current stats.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<CharacterStats> GetStatsAsync();

    /// <summary>
    /// Sets the character's base stats.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<CharacterStats> SetStatsAsync(int level, int strength, int dexterity, int intelligence);

    /// <summary>
    /// Adds stat points to the character.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<CharacterStats> AddStatsAsync(int strength = 0, int dexterity = 0, int intelligence = 0);

    /// <summary>
    /// Sets the character's level.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<CharacterStats> SetLevelAsync(int level);

    #endregion

    #region Bag (Grid Inventory)

    /// <summary>
    /// Gets the bag's grid state.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<InventoryGrid> GetBagGridAsync();

    /// <summary>
    /// Gets all items in the bag.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<IReadOnlyDictionary<Guid, Item>> GetBagItemsAsync();

    /// <summary>
    /// Adds an item to the bag at a specific position.
    /// Returns false if the position is invalid or occupied.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<bool> AddToBagAsync(Item item, int x, int y);

    /// <summary>
    /// Adds an item to the bag at the first available position.
    /// Returns the position, or null if no space available.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<(int X, int Y)?> AddToBagAutoAsync(Item item);

    /// <summary>
    /// Moves an item within the bag to a new position.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<bool> MoveBagItemAsync(Guid itemId, int newX, int newY);

    /// <summary>
    /// Removes an item from the bag.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<Item?> RemoveFromBagAsync(Guid itemId);

    /// <summary>
    /// Checks if there's space for an item with the given dimensions.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<bool> HasSpaceAsync(int width, int height);

    #endregion

    #region Equipment

    /// <summary>
    /// Gets all equipped items.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<IReadOnlyDictionary<EquipmentSlot, Item>> GetEquippedAsync();

    /// <summary>
    /// Gets the item equipped in a specific slot.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<Item?> GetEquippedAsync(EquipmentSlot slot);

    /// <summary>
    /// Attempts to equip an item from the bag.
    /// Validates requirements and handles slot swapping.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<EquipResult> EquipAsync(Guid bagItemId, EquipmentSlot slot);

    /// <summary>
    /// Unequips an item from a slot to the bag.
    /// Returns null if slot is empty or no bag space.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<Item?> UnequipAsync(EquipmentSlot slot);

    /// <summary>
    /// Swaps an equipped item with a bag item.
    /// </summary>
    [Transaction(TransactionOption.CreateOrJoin)]
    Task<EquipResult> SwapEquipAsync(Guid bagItemId, EquipmentSlot slot);

    #endregion

    #region Trading (Transactional)

    /// <summary>
    /// Transfers an item out of the inventory for trading.
    /// Must be called within a transaction.
    /// </summary>
    [Transaction(TransactionOption.Join)]
    Task<Item?> TransferOutAsync(Guid itemId);

    /// <summary>
    /// Transfers an item into the inventory from trading.
    /// Must be called within a transaction.
    /// </summary>
    [Transaction(TransactionOption.Join)]
    Task<bool> TransferInAsync(Item item, int x, int y);

    #endregion
}
