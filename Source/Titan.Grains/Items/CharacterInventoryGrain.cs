using MemoryPack;
using Orleans;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Helpers;
using Titan.Abstractions.Models.Items;

// Note: ItemEventTypes is in Titan.Abstractions.Models.Items

namespace Titan.Grains.Items;

/// <summary>
/// Grain state for CharacterInventoryGrain.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial class CharacterInventoryState
{
    [Id(0), MemoryPackOrder(0)]
    public CharacterStats Stats { get; set; } = new();

    [Id(1), MemoryPackOrder(1)]
    public InventoryGrid BagGrid { get; set; } = InventoryGrid.Create(12, 5);

    [Id(2), MemoryPackOrder(2)]
    public Dictionary<Guid, Item> BagItems { get; set; } = new();

    [Id(3), MemoryPackOrder(3)]
    public Dictionary<EquipmentSlot, Item> Equipped { get; set; } = new();
}

/// <summary>
/// Per-character inventory grain with grid-based storage and equipment.
/// </summary>
public class CharacterInventoryGrain : Grain, ICharacterInventoryGrain
{
    private readonly ITransactionalState<CharacterInventoryState> _state;
    private readonly IGrainFactory _grainFactory;

    public CharacterInventoryGrain(
        [TransactionalState("characterInventory", "TransactionStore")]
        ITransactionalState<CharacterInventoryState> state,
        IGrainFactory grainFactory)
    {
        _state = state;
        _grainFactory = grainFactory;
    }

    #region Stats

    public Task<CharacterStats> GetStatsAsync()
    {
        return _state.PerformRead(s => s.Stats);
    }

    public Task<CharacterStats> SetStatsAsync(int level, int strength, int dexterity, int intelligence)
    {
        return _state.PerformUpdate(s =>
        {
            s.Stats = new CharacterStats
            {
                Level = level,
                Strength = strength,
                Dexterity = dexterity,
                Intelligence = intelligence
            };
            return s.Stats;
        });
    }

    public Task<CharacterStats> AddStatsAsync(int strength = 0, int dexterity = 0, int intelligence = 0)
    {
        return _state.PerformUpdate(s =>
        {
            s.Stats = s.Stats with
            {
                Strength = s.Stats.Strength + strength,
                Dexterity = s.Stats.Dexterity + dexterity,
                Intelligence = s.Stats.Intelligence + intelligence
            };
            return s.Stats;
        });
    }

    public Task<CharacterStats> SetLevelAsync(int level)
    {
        return _state.PerformUpdate(s =>
        {
            s.Stats = s.Stats with { Level = level };
            return s.Stats;
        });
    }

    #endregion

    #region Bag

    public Task<InventoryGrid> GetBagGridAsync()
    {
        return _state.PerformRead(s => s.BagGrid);
    }

    public Task<IReadOnlyDictionary<Guid, Item>> GetBagItemsAsync()
    {
        return _state.PerformRead(s => (IReadOnlyDictionary<Guid, Item>)s.BagItems);
    }

    public async Task<bool> AddToBagAsync(Item item, int x, int y)
    {
        var baseType = await GetBaseTypeAsync(item.BaseTypeId);
        if (baseType == null) return false;

        var width = baseType.Width;
        var height = baseType.Height;

        return await _state.PerformUpdate(s =>
        {
            if (!InventoryGridHelper.CanPlace(s.BagGrid, x, y, width, height))
                return false;

            InventoryGridHelper.Place(s.BagGrid, item.Id, x, y, width, height);
            s.BagItems[item.Id] = item;
            return true;
        });
    }

    public async Task<(int X, int Y)?> AddToBagAutoAsync(Item item)
    {
        var baseType = await GetBaseTypeAsync(item.BaseTypeId);
        if (baseType == null) return null;

        var width = baseType.Width;
        var height = baseType.Height;

        return await _state.PerformUpdate(s =>
        {
            var position = InventoryGridHelper.FindSpace(s.BagGrid, width, height);
            if (position == null) return (ValueTuple<int, int>?)null;

            InventoryGridHelper.Place(s.BagGrid, item.Id, position.Value.X, position.Value.Y, width, height);
            s.BagItems[item.Id] = item;
            return position;
        });
    }

    public async Task<bool> MoveBagItemAsync(Guid itemId, int newX, int newY)
    {
        // Get item info first
        var itemInfo = await _state.PerformRead(s =>
            s.BagItems.TryGetValue(itemId, out var item) ? item : null);

        if (itemInfo == null) return false;

        var baseType = await GetBaseTypeAsync(itemInfo.BaseTypeId);
        if (baseType == null) return false;

        var width = baseType.Width;
        var height = baseType.Height;

        return await _state.PerformUpdate(s =>
        {
            if (!s.BagItems.ContainsKey(itemId))
                return false;

            return InventoryGridHelper.Move(s.BagGrid, itemId, newX, newY, width, height);
        });
    }

    public async Task<Item?> RemoveFromBagAsync(Guid itemId)
    {
        // Get item info first
        var itemInfo = await _state.PerformRead(s =>
            s.BagItems.TryGetValue(itemId, out var item) ? item : null);

        if (itemInfo == null) return null;

        var baseType = await GetBaseTypeAsync(itemInfo.BaseTypeId);
        if (baseType == null) return null;

        var width = baseType.Width;
        var height = baseType.Height;

        return await _state.PerformUpdate(s =>
        {
            if (!s.BagItems.TryGetValue(itemId, out var item))
                return null;

            InventoryGridHelper.Remove(s.BagGrid, itemId, width, height);
            s.BagItems.Remove(itemId);
            return item;
        });
    }

    public Task<bool> HasSpaceAsync(int width, int height)
    {
        return _state.PerformRead(s =>
            InventoryGridHelper.FindSpace(s.BagGrid, width, height) != null);
    }

    #endregion

    #region Equipment

    public Task<IReadOnlyDictionary<EquipmentSlot, Item>> GetEquippedAsync()
    {
        return _state.PerformRead(s => (IReadOnlyDictionary<EquipmentSlot, Item>)s.Equipped);
    }

    public Task<Item?> GetEquippedAsync(EquipmentSlot slot)
    {
        return _state.PerformRead(s => s.Equipped.TryGetValue(slot, out var item) ? item : null);
    }

    public async Task<EquipResult> EquipAsync(Guid bagItemId, EquipmentSlot slot)
    {
        // Get item info first
        var itemInfo = await _state.PerformRead(s =>
            s.BagItems.TryGetValue(bagItemId, out var item) ? item : null);

        if (itemInfo == null) return EquipResult.Failed("Item not found in bag");

        var baseType = await GetBaseTypeAsync(itemInfo.BaseTypeId);
        if (baseType == null) return EquipResult.Failed("Invalid base type");

        // Check slot validity
        if (!EquipmentValidator.IsSlotValid(baseType, slot))
            return EquipResult.Failed($"Item cannot be equipped in {slot}");

        // Get current stats for requirement check
        var stats = await _state.PerformRead(s => s.Stats);

        // Check requirements
        var reqCheck = EquipmentValidator.CanEquip(stats, baseType);
        if (!reqCheck.CanEquip)
            return EquipResult.Failed(reqCheck.FailedRequirements.ToArray());

        // Check for existing equipped item
        var existingItem = await _state.PerformRead(s =>
            s.Equipped.TryGetValue(slot, out var item) ? item : null);

        BaseType? existingBaseType = null;
        if (existingItem != null)
        {
            existingBaseType = await GetBaseTypeAsync(existingItem.BaseTypeId);
        }

        var itemWidth = baseType.Width;
        var itemHeight = baseType.Height;
        var existingWidth = existingBaseType?.Width ?? 0;
        var existingHeight = existingBaseType?.Height ?? 0;

        var result = await _state.PerformUpdate(s =>
        {
            // Re-validate inside transaction
            if (!s.BagItems.TryGetValue(bagItemId, out var item))
                return EquipResult.Failed("Item no longer in bag");

            // Remove from bag
            InventoryGridHelper.Remove(s.BagGrid, bagItemId, itemWidth, itemHeight);
            s.BagItems.Remove(bagItemId);

            // Handle existing equipped item
            Item? unequipped = null;
            if (s.Equipped.TryGetValue(slot, out var existing))
            {
                unequipped = existing;
                if (existingBaseType != null)
                {
                    // Try to put in bag
                    var bagPos = InventoryGridHelper.FindSpace(s.BagGrid, existingWidth, existingHeight);
                    if (bagPos == null)
                    {
                        // No space - put original item back and fail
                        InventoryGridHelper.Place(s.BagGrid, bagItemId, 0, 0, itemWidth, itemHeight);
                        s.BagItems[bagItemId] = item;
                        return EquipResult.Failed("No bag space for unequipped item");
                    }
                    InventoryGridHelper.Place(s.BagGrid, existing.Id, bagPos.Value.X, bagPos.Value.Y,
                                               existingWidth, existingHeight);
                    s.BagItems[existing.Id] = existing;
                }
            }

            s.Equipped[slot] = item;
            return EquipResult.Succeeded(item, unequipped);
        });

        // Record history if successful
        if (result.Success)
        {
            await RecordHistoryAsync(bagItemId, ItemEventTypes.Equipped, new Dictionary<string, string>
            {
                ["slot"] = slot.ToString()
            });
            
            // Also record unequip for the item that was swapped out
            if (result.UnequippedItem != null)
            {
                await RecordHistoryAsync(result.UnequippedItem.Id, ItemEventTypes.Unequipped, new Dictionary<string, string>
                {
                    ["slot"] = slot.ToString(),
                    ["reason"] = "swapped"
                });
            }
        }

        return result;
    }

    public async Task<Item?> UnequipAsync(EquipmentSlot slot)
    {
        // Get equipped item info first
        var item = await _state.PerformRead(s =>
            s.Equipped.TryGetValue(slot, out var equip) ? equip : null);

        if (item == null) return null;

        var baseType = await GetBaseTypeAsync(item.BaseTypeId);
        if (baseType == null) return null;

        var width = baseType.Width;
        var height = baseType.Height;

        var result = await _state.PerformUpdate(s =>
        {
            if (!s.Equipped.TryGetValue(slot, out var equipped))
                return null;

            // Find bag space
            var bagPos = InventoryGridHelper.FindSpace(s.BagGrid, width, height);
            if (bagPos == null)
                return null; // No space

            s.Equipped.Remove(slot);
            InventoryGridHelper.Place(s.BagGrid, equipped.Id, bagPos.Value.X, bagPos.Value.Y, width, height);
            s.BagItems[equipped.Id] = equipped;
            return equipped;
        });

        // Record history if successful
        if (result != null)
        {
            await RecordHistoryAsync(result.Id, ItemEventTypes.Unequipped, new Dictionary<string, string>
            {
                ["slot"] = slot.ToString()
            });
        }

        return result;
    }

    public async Task<EquipResult> SwapEquipAsync(Guid bagItemId, EquipmentSlot slot)
    {
        // Delegate to EquipAsync which handles swapping
        return await EquipAsync(bagItemId, slot);
    }

    #endregion

    #region Trading

    public async Task<Item?> TransferOutAsync(Guid itemId)
    {
        // Get item info first
        var bagItem = await _state.PerformRead(s =>
            s.BagItems.TryGetValue(itemId, out var item) ? item : null);

        if (bagItem != null)
        {
            var baseType = await GetBaseTypeAsync(bagItem.BaseTypeId);
            var width = baseType?.Width ?? 1;
            var height = baseType?.Height ?? 1;

            return await _state.PerformUpdate(s =>
            {
                if (!s.BagItems.TryGetValue(itemId, out var item))
                    return null;

                InventoryGridHelper.Remove(s.BagGrid, itemId, width, height);
                s.BagItems.Remove(itemId);
                return item;
            });
        }

        // Check equipment
        return await _state.PerformUpdate(s =>
        {
            var slotEntry = s.Equipped.FirstOrDefault(e => e.Value.Id == itemId);
            if (slotEntry.Value != null)
            {
                s.Equipped.Remove(slotEntry.Key);
                return slotEntry.Value;
            }
            return null;
        });
    }

    public async Task<bool> TransferInAsync(Item item, int x, int y)
    {
        return await AddToBagAsync(item, x, y);
    }

    #endregion

    private async Task<BaseType?> GetBaseTypeAsync(string baseTypeId)
    {
        var reader = _grainFactory.GetGrain<IBaseTypeReaderGrain>("default");
        return await reader.GetAsync(baseTypeId);
    }

    private Task RecordHistoryAsync(Guid itemId, string eventType, Dictionary<string, string>? details = null)
    {
        // Get character and account info from grain key
        var characterId = this.GetPrimaryKey(out var seasonId);
        
        var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(itemId);
        return historyGrain.RecordEventAsync(
            eventType,
            actorAccountId: null, // Account ID not available at grain level
            actorCharacterId: characterId,
            details: details);
    }
}
