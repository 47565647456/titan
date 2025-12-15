using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Concurrency;
using Titan.Abstractions;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;

namespace Titan.Grains.Items;

/// <summary>
/// Stateless worker for high-performance base type lookups.
/// </summary>
[StatelessWorker]
public class BaseTypeReaderGrain : Grain, IBaseTypeReaderGrain
{
    private readonly IGrainFactory _grainFactory;
    private readonly TimeSpan _cacheDuration;
    private Dictionary<string, BaseType>? _cache;
    private DateTime _cacheExpiry = DateTime.MinValue;

    public BaseTypeReaderGrain(IGrainFactory grainFactory, IOptions<ItemRegistryCacheOptions> options)
    {
        _grainFactory = grainFactory;
        _cacheDuration = options.Value.CacheDuration;
    }

    public async Task<BaseType?> GetAsync(string baseTypeId)
    {
        await EnsureCacheAsync();
        _cache!.TryGetValue(baseTypeId, out var baseType);
        return baseType;
    }

    public async Task<IReadOnlyList<BaseType>> GetByTagsAsync(params string[] tags)
    {
        await EnsureCacheAsync();
        var tagSet = new HashSet<string>(tags);
        return _cache!.Values
            .Where(bt => tagSet.All(t => bt.Tags.Contains(t)))
            .ToList();
    }

    public async Task<(int Width, int Height)> GetGridSizeAsync(string baseTypeId)
    {
        var baseType = await GetAsync(baseTypeId);
        return baseType != null ? (baseType.Width, baseType.Height) : (1, 1);
    }

    public async Task<(int Level, int Str, int Dex, int Int)> GetRequirementsAsync(string baseTypeId)
    {
        var baseType = await GetAsync(baseTypeId);
        return baseType != null
            ? (baseType.RequiredLevel, baseType.RequiredStrength, baseType.RequiredDexterity, baseType.RequiredIntelligence)
            : (0, 0, 0, 0);
    }

    private async Task EnsureCacheAsync()
    {
        // If cache duration is zero, always refresh (test mode)
        if (_cacheDuration == TimeSpan.Zero || _cache == null || DateTime.UtcNow > _cacheExpiry)
        {
            var registry = _grainFactory.GetGrain<IBaseTypeRegistryGrain>("default");
            var all = await registry.GetAllAsync();
            _cache = all.ToDictionary(bt => bt.BaseTypeId);
            _cacheExpiry = DateTime.UtcNow.Add(_cacheDuration);
        }
    }
}
