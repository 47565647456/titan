using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for BaseTypeHub operations.
/// Used with TypedSignalR.Client source generator.
/// </summary>
public interface IBaseTypeHubClient
{
    /// <summary>
    /// Get a base type definition by ID.
    /// </summary>
    Task<BaseType?> GetBaseType(string baseTypeId);

    /// <summary>
    /// Get all base type definitions.
    /// </summary>
    Task<IReadOnlyList<BaseType>> GetAllBaseTypes();

    /// <summary>
    /// Get base types by category.
    /// </summary>
    Task<IReadOnlyList<BaseType>> GetBaseTypesByCategory(ItemCategory category);

    /// <summary>
    /// Get base types by equipment slot.
    /// </summary>
    Task<IReadOnlyList<BaseType>> GetBaseTypesBySlot(EquipmentSlot slot);

    /// <summary>
    /// Register a new base type (admin only).
    /// </summary>
    Task<BaseType> RegisterBaseType(BaseType baseType);

    /// <summary>
    /// Update an existing base type (admin only).
    /// </summary>
    Task<BaseType> UpdateBaseType(BaseType baseType);

    /// <summary>
    /// Delete a base type (admin only).
    /// </summary>
    Task<bool> DeleteBaseType(string baseTypeId);
}
