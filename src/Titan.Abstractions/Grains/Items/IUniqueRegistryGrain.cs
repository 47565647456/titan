using Orleans;
using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Grains.Items;

/// <summary>
/// Singleton grain for managing unique item templates.
/// Key: "default"
/// </summary>
public interface IUniqueRegistryGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets all registered unique definitions.
    /// </summary>
    Task<IReadOnlyList<UniqueDefinition>> GetAllAsync();

    /// <summary>
    /// Gets a unique definition by ID.
    /// </summary>
    Task<UniqueDefinition?> GetAsync(string uniqueId);

    /// <summary>
    /// Registers a new unique definition.
    /// </summary>
    Task RegisterAsync(UniqueDefinition unique);

    /// <summary>
    /// Registers multiple unique definitions.
    /// </summary>
    Task RegisterManyAsync(IEnumerable<UniqueDefinition> uniques);

    /// <summary>
    /// Updates an existing unique definition.
    /// </summary>
    Task UpdateAsync(UniqueDefinition unique);

    /// <summary>
    /// Deletes a unique definition.
    /// </summary>
    Task DeleteAsync(string uniqueId);

    /// <summary>
    /// Gets unique definitions by base type.
    /// </summary>
    Task<IReadOnlyList<UniqueDefinition>> GetByBaseTypeAsync(string baseTypeId);

    /// <summary>
    /// Gets unique definitions that can drop at the specified item level.
    /// </summary>
    Task<IReadOnlyList<UniqueDefinition>> GetByItemLevelAsync(int itemLevel);
}
