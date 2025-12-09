using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Titan.Abstractions;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Inventory;

public class InventoryGrainState
{
    public List<Item> Items { get; set; } = new();
}

public class InventoryGrain : Grain, IInventoryGrain
{
    private readonly IPersistentState<InventoryGrainState> _state;
    private readonly IGrainFactory _grainFactory;
    private readonly ItemRegistryOptions _registryOptions;

    public InventoryGrain(
        [PersistentState("inventory", "OrleansStorage")] IPersistentState<InventoryGrainState> state,
        IGrainFactory grainFactory,
        IOptions<ItemRegistryOptions> registryOptions)
    {
        _state = state;
        _grainFactory = grainFactory;
        _registryOptions = registryOptions.Value;
    }

    private (Guid CharacterId, string SeasonId) GetKey()
    {
        var characterId = this.GetPrimaryKey(out var seasonId);
        return (characterId, seasonId!);
    }

    public Task<List<Item>> GetItemsAsync()
    {
        return Task.FromResult(_state.State.Items);
    }

    public Task<Item?> GetItemAsync(Guid itemId)
    {
        var item = _state.State.Items.FirstOrDefault(i => i.Id == itemId);
        return Task.FromResult(item);
    }

    public async Task<Item> AddItemAsync(string itemTypeId, int quantity = 1, Dictionary<string, object>? metadata = null)
    {
        // Validate against registry
        var registry = _grainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        var definition = await registry.GetAsync(itemTypeId);

        if (definition == null)
        {
            if (!_registryOptions.AllowUnknownItemTypes)
                throw new InvalidOperationException($"Unknown item type: '{itemTypeId}'. Register it in the ItemTypeRegistry first.");
            
            // Use defaults for unknown types
            definition = new ItemTypeDefinition
            {
                ItemTypeId = itemTypeId,
                Name = itemTypeId,
                MaxStackSize = 999,  // Permissive default for unknown types
                IsTradeable = true
            };
        }

        // Enforce max stack size
        if (quantity > definition.MaxStackSize)
            throw new InvalidOperationException($"Quantity {quantity} exceeds max stack size of {definition.MaxStackSize} for '{itemTypeId}'.");

        if (quantity < 1)
            throw new ArgumentException("Quantity must be at least 1.", nameof(quantity));

        var item = new Item
        {
            Id = Guid.NewGuid(),
            ItemTypeId = itemTypeId,
            Quantity = quantity,
            Metadata = metadata,
            AcquiredAt = DateTimeOffset.UtcNow
        };

        _state.State.Items.Add(item);
        await _state.WriteStateAsync();

        // Record history
        var (characterId, _) = GetKey();
        var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(item.Id);
        await historyGrain.AddEntryAsync("Created", characterId);

        return item;
    }

    public async Task ReceiveItemAsync(Item item)
    {
        if (_state.State.Items.Any(i => i.Id == item.Id))
            return;

        _state.State.Items.Add(item);
        await _state.WriteStateAsync();
    }

    public async Task<bool> RemoveItemAsync(Guid itemId)
    {
        var item = _state.State.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
            return false;

        _state.State.Items.Remove(item);
        await _state.WriteStateAsync();

        return true;
    }

    public Task<bool> HasItemAsync(Guid itemId)
    {
        var exists = _state.State.Items.Any(i => i.Id == itemId);
        return Task.FromResult(exists);
    }
}
