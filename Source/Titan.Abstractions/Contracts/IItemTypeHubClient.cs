using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for ItemTypeHub operations.
/// Used with TypedSignalR.Client source generator.
/// </summary>
public interface IItemTypeHubClient
{
    /// <summary>
    /// Get an item type definition by ID.
    /// </summary>
    Task<ItemTypeDefinition?> GetItemType(string itemTypeId);

    /// <summary>
    /// Get all item type definitions.
    /// </summary>
    Task<IReadOnlyList<ItemTypeDefinition>> GetAllItemTypes();

    /// <summary>
    /// Get item types by category.
    /// </summary>
    Task<IReadOnlyList<ItemTypeDefinition>> GetItemTypesByCategory(string category);

    /// <summary>
    /// Register a new item type (admin only).
    /// </summary>
    Task<ItemTypeDefinition> RegisterItemType(ItemTypeDefinition definition);

    /// <summary>
    /// Update an existing item type (admin only).
    /// </summary>
    Task<ItemTypeDefinition> UpdateItemType(ItemTypeDefinition definition);

    /// <summary>
    /// Delete an item type (admin only).
    /// </summary>
    Task<bool> DeleteItemType(string itemTypeId);
}
