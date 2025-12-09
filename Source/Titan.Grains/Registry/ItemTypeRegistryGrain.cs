using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Registry;

/// <summary>
/// State for the ItemTypeRegistry grain.
/// </summary>
public class ItemTypeRegistryState
{
    public Dictionary<string, ItemTypeDefinition> Definitions { get; set; } = new();
}

/// <summary>
/// Grain implementation for the item type registry/catalogue.
/// Singleton grain storing all valid item type definitions.
/// </summary>
public class ItemTypeRegistryGrain : Grain, IItemTypeRegistryGrain
{
    private readonly IPersistentState<ItemTypeRegistryState> _state;

    public ItemTypeRegistryGrain(
        [PersistentState("registry", "OrleansStorage")] IPersistentState<ItemTypeRegistryState> state)
    {
        _state = state;
    }

    // Read operations

    public Task<ItemTypeDefinition?> GetAsync(string itemTypeId)
    {
        _state.State.Definitions.TryGetValue(itemTypeId, out var definition);
        return Task.FromResult(definition);
    }

    public Task<IReadOnlyList<ItemTypeDefinition>> GetAllAsync()
    {
        IReadOnlyList<ItemTypeDefinition> result = _state.State.Definitions.Values.ToList();
        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(string itemTypeId)
    {
        return Task.FromResult(_state.State.Definitions.ContainsKey(itemTypeId));
    }

    public Task<int> GetMaxStackSizeAsync(string itemTypeId)
    {
        if (_state.State.Definitions.TryGetValue(itemTypeId, out var definition))
            return Task.FromResult(definition.MaxStackSize);
        return Task.FromResult(1); // Default to non-stackable
    }

    public Task<bool> IsTradeableAsync(string itemTypeId)
    {
        if (_state.State.Definitions.TryGetValue(itemTypeId, out var definition))
            return Task.FromResult(definition.IsTradeable);
        return Task.FromResult(true); // Default to tradeable
    }

    // Write operations

    public async Task RegisterAsync(ItemTypeDefinition definition)
    {
        if (definition.MaxStackSize < 1)
            throw new ArgumentException("MaxStackSize must be at least 1", nameof(definition));

        _state.State.Definitions[definition.ItemTypeId] = definition;
        await _state.WriteStateAsync();
    }

    public async Task RegisterManyAsync(IEnumerable<ItemTypeDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            if (definition.MaxStackSize < 1)
                throw new ArgumentException($"MaxStackSize must be at least 1 for {definition.ItemTypeId}");

            _state.State.Definitions[definition.ItemTypeId] = definition;
        }
        await _state.WriteStateAsync();
    }

    public async Task UpdateAsync(ItemTypeDefinition definition)
    {
        if (!_state.State.Definitions.ContainsKey(definition.ItemTypeId))
            throw new InvalidOperationException($"Item type '{definition.ItemTypeId}' not found");

        if (definition.MaxStackSize < 1)
            throw new ArgumentException("MaxStackSize must be at least 1", nameof(definition));

        _state.State.Definitions[definition.ItemTypeId] = definition;
        await _state.WriteStateAsync();
    }

    public async Task DeleteAsync(string itemTypeId)
    {
        if (_state.State.Definitions.Remove(itemTypeId))
        {
            await _state.WriteStateAsync();
        }
    }
}
