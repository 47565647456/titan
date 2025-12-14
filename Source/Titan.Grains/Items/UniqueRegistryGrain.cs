using MemoryPack;
using Orleans;
using Orleans.Runtime;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;

namespace Titan.Grains.Items;

/// <summary>
/// Grain state for UniqueRegistryGrain.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial class UniqueRegistryState
{
    [Id(0), MemoryPackOrder(0)]
    public Dictionary<string, UniqueDefinition> Uniques { get; set; } = new();
}

/// <summary>
/// Singleton grain for managing unique item templates.
/// </summary>
public class UniqueRegistryGrain : Grain, IUniqueRegistryGrain
{
    private readonly IPersistentState<UniqueRegistryState> _state;

    public UniqueRegistryGrain(
        [PersistentState("uniqueRegistry", "TransactionStore")]
        IPersistentState<UniqueRegistryState> state)
    {
        _state = state;
    }

    public Task<IReadOnlyList<UniqueDefinition>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyList<UniqueDefinition>>(_state.State.Uniques.Values.ToList());
    }

    public Task<UniqueDefinition?> GetAsync(string uniqueId)
    {
        _state.State.Uniques.TryGetValue(uniqueId, out var unique);
        return Task.FromResult(unique);
    }

    public async Task RegisterAsync(UniqueDefinition unique)
    {
        ValidateUnique(unique);
        _state.State.Uniques[unique.UniqueId] = unique;
        await _state.WriteStateAsync();
    }

    public async Task RegisterManyAsync(IEnumerable<UniqueDefinition> uniques)
    {
        foreach (var unique in uniques)
        {
            ValidateUnique(unique);
            _state.State.Uniques[unique.UniqueId] = unique;
        }
        await _state.WriteStateAsync();
    }

    public async Task UpdateAsync(UniqueDefinition unique)
    {
        if (!_state.State.Uniques.ContainsKey(unique.UniqueId))
            throw new ArgumentException($"Unique '{unique.UniqueId}' not found");

        ValidateUnique(unique);
        _state.State.Uniques[unique.UniqueId] = unique;
        await _state.WriteStateAsync();
    }

    public async Task DeleteAsync(string uniqueId)
    {
        _state.State.Uniques.Remove(uniqueId);
        await _state.WriteStateAsync();
    }

    public Task<IReadOnlyList<UniqueDefinition>> GetByBaseTypeAsync(string baseTypeId)
    {
        var results = _state.State.Uniques.Values
            .Where(u => u.BaseTypeId == baseTypeId)
            .ToList();
        return Task.FromResult<IReadOnlyList<UniqueDefinition>>(results);
    }

    public Task<IReadOnlyList<UniqueDefinition>> GetByItemLevelAsync(int itemLevel)
    {
        var results = _state.State.Uniques.Values
            .Where(u => u.RequiredItemLevel <= itemLevel)
            .ToList();
        return Task.FromResult<IReadOnlyList<UniqueDefinition>>(results);
    }

    private static void ValidateUnique(UniqueDefinition unique)
    {
        if (string.IsNullOrWhiteSpace(unique.UniqueId))
            throw new ArgumentException("UniqueId is required");
        if (string.IsNullOrWhiteSpace(unique.Name))
            throw new ArgumentException("Name is required");
        if (string.IsNullOrWhiteSpace(unique.BaseTypeId))
            throw new ArgumentException("BaseTypeId is required");
        if (unique.Modifiers == null || unique.Modifiers.Length == 0)
            throw new ArgumentException("At least one modifier is required");
    }
}
