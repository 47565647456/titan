using MemoryPack;
using Orleans;
using Orleans.Runtime;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;

namespace Titan.Grains.Items;

/// <summary>
/// Grain state for BaseTypeRegistryGrain.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial class BaseTypeRegistryState
{
    [Id(0), MemoryPackOrder(0)]
    public Dictionary<string, BaseType> BaseTypes { get; set; } = new();
}

/// <summary>
/// Singleton grain for managing base type definitions.
/// </summary>
public class BaseTypeRegistryGrain : Grain, IBaseTypeRegistryGrain
{
    private readonly IPersistentState<BaseTypeRegistryState> _state;

    public BaseTypeRegistryGrain(
        [PersistentState("baseTypeRegistry", "TransactionStore")]
        IPersistentState<BaseTypeRegistryState> state)
    {
        _state = state;
    }

    public Task<IReadOnlyList<BaseType>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyList<BaseType>>(_state.State.BaseTypes.Values.ToList());
    }

    public Task<BaseType?> GetAsync(string baseTypeId)
    {
        _state.State.BaseTypes.TryGetValue(baseTypeId, out var baseType);
        return Task.FromResult(baseType);
    }

    public async Task RegisterAsync(BaseType baseType)
    {
        ValidateBaseType(baseType);
        _state.State.BaseTypes[baseType.BaseTypeId] = baseType;
        await _state.WriteStateAsync();
    }

    public async Task RegisterManyAsync(IEnumerable<BaseType> baseTypes)
    {
        foreach (var baseType in baseTypes)
        {
            ValidateBaseType(baseType);
            _state.State.BaseTypes[baseType.BaseTypeId] = baseType;
        }
        await _state.WriteStateAsync();
    }

    public async Task UpdateAsync(BaseType baseType)
    {
        if (!_state.State.BaseTypes.ContainsKey(baseType.BaseTypeId))
            throw new ArgumentException($"Base type '{baseType.BaseTypeId}' not found");

        ValidateBaseType(baseType);
        _state.State.BaseTypes[baseType.BaseTypeId] = baseType;
        await _state.WriteStateAsync();
    }

    public async Task DeleteAsync(string baseTypeId)
    {
        _state.State.BaseTypes.Remove(baseTypeId);
        await _state.WriteStateAsync();
    }

    public Task<bool> ExistsAsync(string baseTypeId)
    {
        return Task.FromResult(_state.State.BaseTypes.ContainsKey(baseTypeId));
    }

    public Task<IReadOnlyList<BaseType>> GetByCategoryAsync(ItemCategory category)
    {
        var results = _state.State.BaseTypes.Values
            .Where(bt => bt.Category == category)
            .ToList();
        return Task.FromResult<IReadOnlyList<BaseType>>(results);
    }

    public Task<IReadOnlyList<BaseType>> GetBySlotAsync(EquipmentSlot slot)
    {
        var results = _state.State.BaseTypes.Values
            .Where(bt => bt.Slot == slot)
            .ToList();
        return Task.FromResult<IReadOnlyList<BaseType>>(results);
    }

    private static void ValidateBaseType(BaseType baseType)
    {
        if (string.IsNullOrWhiteSpace(baseType.BaseTypeId))
            throw new ArgumentException("BaseTypeId is required");
        if (string.IsNullOrWhiteSpace(baseType.Name))
            throw new ArgumentException("Name is required");
        if (baseType.Width < 1)
            throw new ArgumentException("Width must be at least 1");
        if (baseType.Height < 1)
            throw new ArgumentException("Height must be at least 1");
        if (baseType.MaxStackSize < 1)
            throw new ArgumentException("MaxStackSize must be at least 1");
    }
}
