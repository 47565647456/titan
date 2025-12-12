using MemoryPack;
using Microsoft.Extensions.Options;
using Orleans.Transactions.Abstractions;
using Titan.Abstractions;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Inventory;

[Serializable]
[GenerateSerializer]
[MemoryPackable]
public partial class InventoryGrainState
{
    [Id(0), MemoryPackOrder(0)]
    public List<Item> Items { get; set; } = new();
}

public class InventoryGrain : Grain, IInventoryGrain
{
    private readonly ITransactionalState<InventoryGrainState> _state;
    private readonly IGrainFactory _grainFactory;
    private readonly ItemRegistryOptions _registryOptions;

    public InventoryGrain(
        [TransactionalState("inventory", "TransactionStore")] ITransactionalState<InventoryGrainState> state,
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
        return _state.PerformRead(state => state.Items.ToList());
    }

    public Task<Item?> GetItemAsync(Guid itemId)
    {
        return _state.PerformRead(state => state.Items.FirstOrDefault(i => i.Id == itemId));
    }

    public async Task<Item> AddItemAsync(string itemTypeId, int quantity = 1, Dictionary<string, string>? metadata = null)
    {
        // Validate against registry using stateless reader (outside transaction for performance)
        var reader = _grainFactory.GetGrain<IItemTypeReaderGrain>("default");
        var definition = await reader.GetAsync(itemTypeId);

        if (definition == null)
        {
            if (!_registryOptions.AllowUnknownItemTypes)
                throw new InvalidOperationException($"Unknown item type: '{itemTypeId}'. Register it in the ItemTypeRegistry first.");
            
            // Use defaults for unknown types
            definition = new ItemTypeDefinition
            {
                ItemTypeId = itemTypeId,
                Name = itemTypeId,
                MaxStackSize = 999,
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

        await _state.PerformUpdate(state => state.Items.Add(item));

        // Record history (outside transaction)
        var (characterId, _) = GetKey();
        var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(item.Id);
        await historyGrain.AddEntryAsync("Created", characterId);

        return item;
    }

    public async Task<bool> RemoveItemAsync(Guid itemId)
    {
        var removed = false;
        await _state.PerformUpdate(state =>
        {
            var item = state.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null)
            {
                state.Items.Remove(item);
                removed = true;
            }
        });
        return removed;
    }

    public Task<bool> HasItemAsync(Guid itemId)
    {
        return _state.PerformRead(state => state.Items.Any(i => i.Id == itemId));
    }

    #region Transaction Methods for Trading

    public async Task<Item?> TransferItemOutAsync(Guid itemId)
    {
        Item? transferredItem = null;
        
        await _state.PerformUpdate(state =>
        {
            var item = state.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null)
            {
                state.Items.Remove(item);
                transferredItem = item;
            }
        });

        return transferredItem;
    }

    public Task TransferItemInAsync(Item item)
    {
        return _state.PerformUpdate(state =>
        {
            // Prevent duplicates
            if (!state.Items.Any(i => i.Id == item.Id))
            {
                state.Items.Add(item);
            }
        });
    }

    #endregion
}


