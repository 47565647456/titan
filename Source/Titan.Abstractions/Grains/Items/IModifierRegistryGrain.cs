using Orleans;
using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Grains.Items;

/// <summary>
/// Singleton grain for managing modifier definitions.
/// Key: "default"
/// </summary>
public interface IModifierRegistryGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets all registered modifiers.
    /// </summary>
    Task<IReadOnlyList<ModifierDefinition>> GetAllAsync();

    /// <summary>
    /// Gets a modifier by ID.
    /// </summary>
    Task<ModifierDefinition?> GetAsync(string modifierId);

    /// <summary>
    /// Registers a new modifier.
    /// </summary>
    Task RegisterAsync(ModifierDefinition modifier);

    /// <summary>
    /// Registers multiple modifiers.
    /// </summary>
    Task RegisterManyAsync(IEnumerable<ModifierDefinition> modifiers);

    /// <summary>
    /// Updates an existing modifier.
    /// </summary>
    Task UpdateAsync(ModifierDefinition modifier);

    /// <summary>
    /// Deletes a modifier.
    /// </summary>
    Task DeleteAsync(string modifierId);

    /// <summary>
    /// Gets modifiers by type (Prefix, Suffix, etc.).
    /// </summary>
    Task<IReadOnlyList<ModifierDefinition>> GetByTypeAsync(ModifierType type);

    /// <summary>
    /// Gets modifiers that can roll on an item with the specified base type and level.
    /// </summary>
    Task<IReadOnlyList<ModifierDefinition>> GetForItemAsync(string baseTypeId, int itemLevel);
}
