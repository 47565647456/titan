using Orleans;
using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Grains.Items;

/// <summary>
/// Stateless worker for high-performance modifier lookups.
/// Implementation should be marked with [StatelessWorker].
/// Key: "default"
/// </summary>
public interface IModifierReaderGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets a modifier by ID.
    /// </summary>
    Task<ModifierDefinition?> GetAsync(string modifierId);

    /// <summary>
    /// Gets available prefixes for an item with the specified base type and level.
    /// Filters by item tags and item level.
    /// </summary>
    Task<IReadOnlyList<ModifierDefinition>> GetAvailablePrefixesAsync(string baseTypeId, int itemLevel);

    /// <summary>
    /// Gets available suffixes for an item with the specified base type and level.
    /// Filters by item tags and item level.
    /// </summary>
    Task<IReadOnlyList<ModifierDefinition>> GetAvailableSuffixesAsync(string baseTypeId, int itemLevel);

    /// <summary>
    /// Rolls a random value for a modifier within its ranges.
    /// </summary>
    Task<RolledModifier> RollModifierAsync(string modifierId);
}
