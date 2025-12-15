using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for InventoryHub operations.
/// Used with TypedSignalR.Client source generator.
/// </summary>
public interface IInventoryHubClient
{
    /// <summary>
    /// Get bag grid state for a character.
    /// </summary>
    Task<InventoryGrid> GetBagGrid(Guid characterId, string seasonId);

    /// <summary>
    /// Get all items in a character's bag.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Item>> GetBagItems(Guid characterId, string seasonId);

    /// <summary>
    /// Get equipped items for a character.
    /// </summary>
    Task<IReadOnlyDictionary<EquipmentSlot, Item>> GetEquipped(Guid characterId, string seasonId);

    /// <summary>
    /// Add a new item to a character's bag at a specific position.
    /// </summary>
    Task<bool> AddToBag(Guid characterId, string seasonId, Item item, int x, int y);

    /// <summary>
    /// Move an item within the bag.
    /// </summary>
    Task<bool> MoveBagItem(Guid characterId, string seasonId, Guid itemId, int newX, int newY);

    /// <summary>
    /// Equip an item from the bag.
    /// </summary>
    Task<EquipResult> Equip(Guid characterId, string seasonId, Guid bagItemId, EquipmentSlot slot);

    /// <summary>
    /// Unequip an item to the bag.
    /// </summary>
    Task<Item?> Unequip(Guid characterId, string seasonId, EquipmentSlot slot);

    /// <summary>
    /// Get character stats.
    /// </summary>
    Task<CharacterStats> GetStats(Guid characterId, string seasonId);
}
