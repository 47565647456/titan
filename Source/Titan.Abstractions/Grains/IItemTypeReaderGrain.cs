using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Stateless worker grain for high-throughput item type reads.
/// Multiple activations can exist across silos, each with local cache.
/// Uses the main ItemTypeRegistryGrain as source of truth.
/// NOTE: Implementation must be decorated with [StatelessWorker(8)]
/// </summary>
public interface IItemTypeReaderGrain : IGrainWithStringKey
{
    /// <summary>
    /// Get an item type definition by ID (cached).
    /// </summary>
    Task<ItemTypeDefinition?> GetAsync(string itemTypeId);

    /// <summary>
    /// Get all registered item type definitions (cached).
    /// </summary>
    Task<IReadOnlyList<ItemTypeDefinition>> GetAllAsync();

    /// <summary>
    /// Check if an item type exists in the registry (cached).
    /// </summary>
    Task<bool> ExistsAsync(string itemTypeId);

    /// <summary>
    /// Get the max stack size for an item type (cached).
    /// </summary>
    Task<int> GetMaxStackSizeAsync(string itemTypeId);

    /// <summary>
    /// Check if an item type is tradeable (cached).
    /// </summary>
    Task<bool> IsTradeableAsync(string itemTypeId);

    /// <summary>
    /// Invalidate the local cache. Called when registry is modified.
    /// </summary>
    Task InvalidateCacheAsync();
}
