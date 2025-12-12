using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Registry;

/// <summary>
/// Stateless worker implementation for high-throughput item type reads.
/// Maintains a local cache refreshed periodically from the main registry.
/// Multiple activations can run concurrently across silos.
/// </summary>
[StatelessWorker(maxLocalWorkers: 8)]
public class ItemTypeReaderGrain : Grain, IItemTypeReaderGrain
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<ItemTypeReaderGrain> _logger;
    
    // Local cache with TTL
    private Dictionary<string, ItemTypeDefinition> _cache = new();
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    
    public ItemTypeReaderGrain(IGrainFactory grainFactory, ILogger<ItemTypeReaderGrain> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    private async Task EnsureCacheAsync()
    {
        if (DateTime.UtcNow < _cacheExpiry && _cache.Count > 0)
            return;

        _logger.LogDebug("Refreshing item type cache from registry");
        
        var registry = _grainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        var items = await registry.GetAllAsync();
        
        _cache = items.ToDictionary(i => i.ItemTypeId);
        _cacheExpiry = DateTime.UtcNow.Add(CacheTtl);
    }

    public async Task<ItemTypeDefinition?> GetAsync(string itemTypeId)
    {
        await EnsureCacheAsync();
        _cache.TryGetValue(itemTypeId, out var definition);
        return definition;
    }

    public async Task<IReadOnlyList<ItemTypeDefinition>> GetAllAsync()
    {
        await EnsureCacheAsync();
        return _cache.Values.ToList();
    }

    public async Task<bool> ExistsAsync(string itemTypeId)
    {
        await EnsureCacheAsync();
        return _cache.ContainsKey(itemTypeId);
    }

    public async Task<int> GetMaxStackSizeAsync(string itemTypeId)
    {
        await EnsureCacheAsync();
        if (_cache.TryGetValue(itemTypeId, out var definition))
            return definition.MaxStackSize;
        return 1; // Default to non-stackable
    }

    public async Task<bool> IsTradeableAsync(string itemTypeId)
    {
        await EnsureCacheAsync();
        if (_cache.TryGetValue(itemTypeId, out var definition))
            return definition.IsTradeable;
        return true; // Default to tradeable
    }

    public Task InvalidateCacheAsync()
    {
        _cache.Clear();
        _cacheExpiry = DateTime.MinValue;
        _logger.LogDebug("Item type cache invalidated");
        return Task.CompletedTask;
    }
}
