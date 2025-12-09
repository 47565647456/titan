using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing the item type registry/catalogue.
/// Singleton grain (key: "default") storing all valid item type definitions.
/// </summary>
public interface IItemTypeRegistryGrain : IGrainWithStringKey
{
    // Read operations

    /// <summary>
    /// Get an item type definition by ID.
    /// </summary>
    Task<ItemTypeDefinition?> GetAsync(string itemTypeId);

    /// <summary>
    /// Get all registered item type definitions.
    /// </summary>
    Task<IReadOnlyList<ItemTypeDefinition>> GetAllAsync();

    /// <summary>
    /// Check if an item type exists in the registry.
    /// </summary>
    Task<bool> ExistsAsync(string itemTypeId);

    /// <summary>
    /// Get the max stack size for an item type. Returns 1 if not found.
    /// </summary>
    Task<int> GetMaxStackSizeAsync(string itemTypeId);

    /// <summary>
    /// Check if an item type is tradeable. Returns true if not found.
    /// </summary>
    Task<bool> IsTradeableAsync(string itemTypeId);

    // Write operations

    /// <summary>
    /// Register a new item type definition.
    /// </summary>
    Task RegisterAsync(ItemTypeDefinition definition);

    /// <summary>
    /// Register multiple item type definitions at once.
    /// </summary>
    Task RegisterManyAsync(IEnumerable<ItemTypeDefinition> definitions);

    /// <summary>
    /// Update an existing item type definition.
    /// </summary>
    Task UpdateAsync(ItemTypeDefinition definition);

    /// <summary>
    /// Delete an item type from the registry.
    /// </summary>
    Task DeleteAsync(string itemTypeId);
}
