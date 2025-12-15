using MemoryPack;
using Orleans;
using Orleans.Runtime;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;

namespace Titan.Grains.Items;

/// <summary>
/// Grain state for ModifierRegistryGrain.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial class ModifierRegistryState
{
    [Id(0), MemoryPackOrder(0)]
    public Dictionary<string, ModifierDefinition> Modifiers { get; set; } = new();
}

/// <summary>
/// Singleton grain for managing modifier definitions.
/// </summary>
public class ModifierRegistryGrain : Grain, IModifierRegistryGrain
{
    private readonly IPersistentState<ModifierRegistryState> _state;
    private readonly IGrainFactory _grainFactory;

    public ModifierRegistryGrain(
        [PersistentState("modifierRegistry", "TransactionStore")]
        IPersistentState<ModifierRegistryState> state,
        IGrainFactory grainFactory)
    {
        _state = state;
        _grainFactory = grainFactory;
    }

    public Task<IReadOnlyList<ModifierDefinition>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyList<ModifierDefinition>>(_state.State.Modifiers.Values.ToList());
    }

    public Task<ModifierDefinition?> GetAsync(string modifierId)
    {
        _state.State.Modifiers.TryGetValue(modifierId, out var modifier);
        return Task.FromResult(modifier);
    }

    public async Task RegisterAsync(ModifierDefinition modifier)
    {
        ValidateModifier(modifier);
        _state.State.Modifiers[modifier.ModifierId] = modifier;
        await _state.WriteStateAsync();
    }

    public async Task RegisterManyAsync(IEnumerable<ModifierDefinition> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            ValidateModifier(modifier);
            _state.State.Modifiers[modifier.ModifierId] = modifier;
        }
        await _state.WriteStateAsync();
    }

    public async Task UpdateAsync(ModifierDefinition modifier)
    {
        if (!_state.State.Modifiers.ContainsKey(modifier.ModifierId))
            throw new ArgumentException($"Modifier '{modifier.ModifierId}' not found");

        ValidateModifier(modifier);
        _state.State.Modifiers[modifier.ModifierId] = modifier;
        await _state.WriteStateAsync();
    }

    public async Task DeleteAsync(string modifierId)
    {
        _state.State.Modifiers.Remove(modifierId);
        await _state.WriteStateAsync();
    }

    public Task<IReadOnlyList<ModifierDefinition>> GetByTypeAsync(ModifierType type)
    {
        var results = _state.State.Modifiers.Values
            .Where(m => m.Type == type)
            .ToList();
        return Task.FromResult<IReadOnlyList<ModifierDefinition>>(results);
    }

    public async Task<IReadOnlyList<ModifierDefinition>> GetForItemAsync(string baseTypeId, int itemLevel)
    {
        // Get base type to check tags
        var baseTypeReader = _grainFactory.GetGrain<IBaseTypeReaderGrain>("default");
        var baseType = await baseTypeReader.GetAsync(baseTypeId);
        if (baseType == null)
            return Array.Empty<ModifierDefinition>();

        var results = _state.State.Modifiers.Values
            .Where(m => m.RequiredItemLevel <= itemLevel)
            .Where(m => m.RequiredTags.All(t => baseType.Tags.Contains(t)))
            .Where(m => !m.ExcludedTags.Any(t => baseType.Tags.Contains(t)))
            .ToList();

        return results;
    }

    private static void ValidateModifier(ModifierDefinition modifier)
    {
        if (string.IsNullOrWhiteSpace(modifier.ModifierId))
            throw new ArgumentException("ModifierId is required");
        if (string.IsNullOrWhiteSpace(modifier.DisplayTemplate))
            throw new ArgumentException("DisplayTemplate is required");
        if (modifier.Ranges == null || modifier.Ranges.Length == 0)
            throw new ArgumentException("At least one range is required");
        if (modifier.Weight < 0)
            throw new ArgumentException("Weight cannot be negative");
    }
}
