using Orleans.Runtime;
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

    public InventoryGrain(
        [PersistentState("inventory", "OrleansStorage")] IPersistentState<InventoryGrainState> state,
        IGrainFactory grainFactory)
    {
        _state = state;
        _grainFactory = grainFactory;
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
        var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(item.Id);
        await historyGrain.AddEntryAsync("Created", this.GetPrimaryKey());

        return item;
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
