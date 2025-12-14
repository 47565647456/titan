using Orleans;
using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Grains.Items;

/// <summary>
/// Singleton grain for managing base type definitions.
/// Key: "default"
/// </summary>
public interface IBaseTypeRegistryGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets all registered base types.
    /// </summary>
    Task<IReadOnlyList<BaseType>> GetAllAsync();

    /// <summary>
    /// Gets a base type by ID.
    /// </summary>
    Task<BaseType?> GetAsync(string baseTypeId);

    /// <summary>
    /// Registers a new base type.
    /// </summary>
    Task RegisterAsync(BaseType baseType);

    /// <summary>
    /// Registers multiple base types.
    /// </summary>
    Task RegisterManyAsync(IEnumerable<BaseType> baseTypes);

    /// <summary>
    /// Updates an existing base type.
    /// </summary>
    Task UpdateAsync(BaseType baseType);

    /// <summary>
    /// Deletes a base type.
    /// </summary>
    Task DeleteAsync(string baseTypeId);

    /// <summary>
    /// Checks if a base type exists.
    /// </summary>
    Task<bool> ExistsAsync(string baseTypeId);

    /// <summary>
    /// Gets base types by category.
    /// </summary>
    Task<IReadOnlyList<BaseType>> GetByCategoryAsync(ItemCategory category);

    /// <summary>
    /// Gets base types by equipment slot.
    /// </summary>
    Task<IReadOnlyList<BaseType>> GetBySlotAsync(EquipmentSlot slot);
}
