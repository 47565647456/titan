using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Concurrency;
using Titan.Abstractions;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;

namespace Titan.Grains.Items;

/// <summary>
/// Stateless worker for high-performance modifier lookups.
/// </summary>
[StatelessWorker]
public class ModifierReaderGrain : Grain, IModifierReaderGrain
{
    private readonly IGrainFactory _grainFactory;
    private readonly TimeSpan _cacheDuration;
    private readonly Random _random = new();
    private Dictionary<string, ModifierDefinition>? _cache;
    private DateTime _cacheExpiry = DateTime.MinValue;

    public ModifierReaderGrain(IGrainFactory grainFactory, IOptions<ItemRegistryCacheOptions> options)
    {
        _grainFactory = grainFactory;
        _cacheDuration = options.Value.CacheDuration;
    }

    public async Task<ModifierDefinition?> GetAsync(string modifierId)
    {
        await EnsureCacheAsync();
        _cache!.TryGetValue(modifierId, out var modifier);
        return modifier;
    }

    public async Task<IReadOnlyList<ModifierDefinition>> GetAvailablePrefixesAsync(string baseTypeId, int itemLevel)
    {
        await EnsureCacheAsync();
        var baseTypeReader = _grainFactory.GetGrain<IBaseTypeReaderGrain>("default");
        var baseType = await baseTypeReader.GetAsync(baseTypeId);
        if (baseType == null)
            return Array.Empty<ModifierDefinition>();

        return _cache!.Values
            .Where(m => m.Type == ModifierType.Prefix)
            .Where(m => m.RequiredItemLevel <= itemLevel)
            .Where(m => m.RequiredTags.All(t => baseType.Tags.Contains(t)))
            .Where(m => !m.ExcludedTags.Any(t => baseType.Tags.Contains(t)))
            .ToList();
    }

    public async Task<IReadOnlyList<ModifierDefinition>> GetAvailableSuffixesAsync(string baseTypeId, int itemLevel)
    {
        await EnsureCacheAsync();
        var baseTypeReader = _grainFactory.GetGrain<IBaseTypeReaderGrain>("default");
        var baseType = await baseTypeReader.GetAsync(baseTypeId);
        if (baseType == null)
            return Array.Empty<ModifierDefinition>();

        return _cache!.Values
            .Where(m => m.Type == ModifierType.Suffix)
            .Where(m => m.RequiredItemLevel <= itemLevel)
            .Where(m => m.RequiredTags.All(t => baseType.Tags.Contains(t)))
            .Where(m => !m.ExcludedTags.Any(t => baseType.Tags.Contains(t)))
            .ToList();
    }

    public async Task<RolledModifier> RollModifierAsync(string modifierId)
    {
        var modifier = await GetAsync(modifierId);
        if (modifier == null)
            throw new ArgumentException($"Modifier '{modifierId}' not found");

        // Roll values for each range
        var values = new int[modifier.Ranges.Length];
        for (int i = 0; i < modifier.Ranges.Length; i++)
        {
            var range = modifier.Ranges[i];
            values[i] = _random.Next(range.Min, range.Max + 1);
        }

        // Format display text with rolled values
        var displayText = string.Format(modifier.DisplayTemplate, values.Cast<object>().ToArray());

        return new RolledModifier
        {
            ModifierId = modifierId,
            Values = values,
            DisplayText = displayText
        };
    }

    private async Task EnsureCacheAsync()
    {
        // If cache duration is zero, always refresh (test mode)
        if (_cacheDuration == TimeSpan.Zero || _cache == null || DateTime.UtcNow > _cacheExpiry)
        {
            var registry = _grainFactory.GetGrain<IModifierRegistryGrain>("default");
            var all = await registry.GetAllAsync();
            _cache = all.ToDictionary(m => m.ModifierId);
            _cacheExpiry = DateTime.UtcNow.Add(_cacheDuration);
        }
    }
}
