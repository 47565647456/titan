using MemoryPack;
using Orleans;
using Orleans.Runtime;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Helpers;
using Titan.Abstractions.Models.Items;

namespace Titan.Grains.Items;

/// <summary>
/// State for a single stash tab.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial class StashTabState
{
    [Id(0), MemoryPackOrder(0)]
    public StashTab Metadata { get; set; } = null!;

    [Id(1), MemoryPackOrder(1)]
    public InventoryGrid Grid { get; set; } = null!;

    [Id(2), MemoryPackOrder(2)]
    public Dictionary<Guid, Item> Items { get; set; } = new();

    [Id(3), MemoryPackOrder(3)]
    public Dictionary<Guid, string> ItemPrices { get; set; } = new();
}

/// <summary>
/// Grain state for AccountStashGrain.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial class AccountStashState
{
    [Id(0), MemoryPackOrder(0)]
    public Dictionary<Guid, StashTabState> Tabs { get; set; } = new();

    [Id(1), MemoryPackOrder(1)]
    public List<Guid> TabOrder { get; set; } = new();
}

/// <summary>
/// Per-account stash grain with multiple tabs.
/// </summary>
public class AccountStashGrain : Grain, IAccountStashGrain
{
    private readonly IPersistentState<AccountStashState> _state;
    private readonly IGrainFactory _grainFactory;

    public AccountStashGrain(
        [PersistentState("accountStash", "TransactionStore")]
        IPersistentState<AccountStashState> state,
        IGrainFactory grainFactory)
    {
        _state = state;
        _grainFactory = grainFactory;
    }

    #region Tab Management

    public Task<IReadOnlyList<StashTab>> GetTabsAsync()
    {
        var tabs = _state.State.TabOrder
            .Where(id => _state.State.Tabs.ContainsKey(id))
            .Select(id => _state.State.Tabs[id].Metadata)
            .ToList();
        return Task.FromResult<IReadOnlyList<StashTab>>(tabs);
    }

    public Task<StashTab?> GetTabAsync(Guid tabId)
    {
        return Task.FromResult(_state.State.Tabs.TryGetValue(tabId, out var tab) ? tab.Metadata : null);
    }

    public async Task<StashTab> CreateTabAsync(string name, StashTabType type = StashTabType.General, 
                                                int gridWidth = 12, int gridHeight = 12)
    {
        var tabId = Guid.NewGuid();
        var metadata = new StashTab
        {
            TabId = tabId,
            Name = name,
            Type = type,
            SortOrder = _state.State.TabOrder.Count,
            GridWidth = gridWidth,
            GridHeight = gridHeight
        };

        var tabState = new StashTabState
        {
            Metadata = metadata,
            Grid = InventoryGrid.Create(gridWidth, gridHeight),
            Items = new Dictionary<Guid, Item>(),
            ItemPrices = new Dictionary<Guid, string>()
        };

        _state.State.Tabs[tabId] = tabState;
        _state.State.TabOrder.Add(tabId);
        await _state.WriteStateAsync();

        return metadata;
    }

    public async Task<StashTab?> RenameTabAsync(Guid tabId, string newName)
    {
        if (!_state.State.Tabs.TryGetValue(tabId, out var tab))
            return null;

        tab.Metadata = tab.Metadata with { Name = newName };
        await _state.WriteStateAsync();
        return tab.Metadata;
    }

    public async Task<bool> DeleteTabAsync(Guid tabId)
    {
        if (!_state.State.Tabs.ContainsKey(tabId))
            return false;

        _state.State.Tabs.Remove(tabId);
        _state.State.TabOrder.Remove(tabId);

        // Re-number sort orders
        for (int i = 0; i < _state.State.TabOrder.Count; i++)
        {
            var id = _state.State.TabOrder[i];
            if (_state.State.Tabs.TryGetValue(id, out var t))
            {
                t.Metadata = t.Metadata with { SortOrder = i };
            }
        }

        await _state.WriteStateAsync();
        return true;
    }

    public async Task ReorderTabsAsync(Guid[] tabIdsInOrder)
    {
        _state.State.TabOrder = tabIdsInOrder.Where(id => _state.State.Tabs.ContainsKey(id)).ToList();
        
        for (int i = 0; i < _state.State.TabOrder.Count; i++)
        {
            var id = _state.State.TabOrder[i];
            if (_state.State.Tabs.TryGetValue(id, out var tab))
            {
                tab.Metadata = tab.Metadata with { SortOrder = i };
            }
        }

        await _state.WriteStateAsync();
    }

    public async Task SetTabAffinityAsync(Guid tabId, ItemCategory? affinity)
    {
        if (_state.State.Tabs.TryGetValue(tabId, out var tab))
        {
            tab.Metadata = tab.Metadata with { Affinity = affinity };
            await _state.WriteStateAsync();
        }
    }

    #endregion

    #region Grid Operations

    public Task<InventoryGrid?> GetTabGridAsync(Guid tabId)
    {
        return Task.FromResult(_state.State.Tabs.TryGetValue(tabId, out var tab) ? tab.Grid : null);
    }

    public Task<IReadOnlyDictionary<Guid, Item>> GetTabItemsAsync(Guid tabId)
    {
        if (_state.State.Tabs.TryGetValue(tabId, out var tab))
            return Task.FromResult<IReadOnlyDictionary<Guid, Item>>(tab.Items);
        return Task.FromResult<IReadOnlyDictionary<Guid, Item>>(new Dictionary<Guid, Item>());
    }

    public async Task<bool> DepositAsync(Guid tabId, Item item, int x, int y)
    {
        if (!_state.State.Tabs.TryGetValue(tabId, out var tab))
            return false;

        var baseType = await GetBaseTypeAsync(item.BaseTypeId);
        if (baseType == null) return false;

        if (!InventoryGridHelper.CanPlace(tab.Grid, x, y, baseType.Width, baseType.Height))
            return false;

        InventoryGridHelper.Place(tab.Grid, item.Id, x, y, baseType.Width, baseType.Height);
        tab.Items[item.Id] = item;
        await _state.WriteStateAsync();
        return true;
    }

    public async Task<(int X, int Y)?> DepositAutoAsync(Guid tabId, Item item)
    {
        if (!_state.State.Tabs.TryGetValue(tabId, out var tab))
            return null;

        var baseType = await GetBaseTypeAsync(item.BaseTypeId);
        if (baseType == null) return null;

        var position = InventoryGridHelper.FindSpace(tab.Grid, baseType.Width, baseType.Height);
        if (position == null) return null;

        InventoryGridHelper.Place(tab.Grid, item.Id, position.Value.X, position.Value.Y, 
                                   baseType.Width, baseType.Height);
        tab.Items[item.Id] = item;
        await _state.WriteStateAsync();
        return position;
    }

    public async Task<(Guid TabId, int X, int Y)?> QuickDepositAsync(Item item, ItemCategory category)
    {
        var baseType = await GetBaseTypeAsync(item.BaseTypeId);
        if (baseType == null) return null;

        // Find tab with matching affinity
        foreach (var tabId in _state.State.TabOrder)
        {
            if (!_state.State.Tabs.TryGetValue(tabId, out var tab))
                continue;

            if (tab.Metadata.Affinity == category || tab.Metadata.Affinity == null)
            {
                var position = InventoryGridHelper.FindSpace(tab.Grid, baseType.Width, baseType.Height);
                if (position != null)
                {
                    InventoryGridHelper.Place(tab.Grid, item.Id, position.Value.X, position.Value.Y,
                                               baseType.Width, baseType.Height);
                    tab.Items[item.Id] = item;
                    await _state.WriteStateAsync();
                    return (tabId, position.Value.X, position.Value.Y);
                }
            }
        }

        return null;
    }

    public async Task<Item?> WithdrawAsync(Guid tabId, Guid itemId)
    {
        if (!_state.State.Tabs.TryGetValue(tabId, out var tab))
            return null;

        if (!tab.Items.TryGetValue(itemId, out var item))
            return null;

        var baseType = await GetBaseTypeAsync(item.BaseTypeId);
        if (baseType != null)
        {
            InventoryGridHelper.Remove(tab.Grid, itemId, baseType.Width, baseType.Height);
        }

        tab.Items.Remove(itemId);
        tab.ItemPrices.Remove(itemId);
        await _state.WriteStateAsync();
        return item;
    }

    public async Task<bool> MoveItemAsync(Guid tabId, Guid itemId, int newX, int newY)
    {
        if (!_state.State.Tabs.TryGetValue(tabId, out var tab))
            return false;

        if (!tab.Items.TryGetValue(itemId, out var item))
            return false;

        var baseType = await GetBaseTypeAsync(item.BaseTypeId);
        if (baseType == null) return false;

        if (!InventoryGridHelper.Move(tab.Grid, itemId, newX, newY, baseType.Width, baseType.Height))
            return false;

        await _state.WriteStateAsync();
        return true;
    }

    public async Task<bool> MoveItemBetweenTabsAsync(Guid fromTabId, Guid toTabId, Guid itemId, int x, int y)
    {
        if (!_state.State.Tabs.TryGetValue(fromTabId, out var fromTab) ||
            !_state.State.Tabs.TryGetValue(toTabId, out var toTab))
            return false;

        if (!fromTab.Items.TryGetValue(itemId, out var item))
            return false;

        var baseType = await GetBaseTypeAsync(item.BaseTypeId);
        if (baseType == null) return false;

        // Check target position
        if (!InventoryGridHelper.CanPlace(toTab.Grid, x, y, baseType.Width, baseType.Height))
            return false;

        // Remove from source
        InventoryGridHelper.Remove(fromTab.Grid, itemId, baseType.Width, baseType.Height);
        fromTab.Items.Remove(itemId);
        var price = fromTab.ItemPrices.TryGetValue(itemId, out var p) ? p : null;
        fromTab.ItemPrices.Remove(itemId);

        // Add to target
        InventoryGridHelper.Place(toTab.Grid, itemId, x, y, baseType.Width, baseType.Height);
        toTab.Items[itemId] = item;
        if (price != null)
            toTab.ItemPrices[itemId] = price;

        await _state.WriteStateAsync();
        return true;
    }

    #endregion

    #region Trading

    public async Task SetTabPublicAsync(Guid tabId, bool isPublic)
    {
        if (_state.State.Tabs.TryGetValue(tabId, out var tab))
        {
            if (tab.Metadata.Type != StashTabType.Premium && isPublic)
                throw new InvalidOperationException("Only Premium tabs can be made public");

            tab.Metadata = tab.Metadata with { IsPublic = isPublic };
            await _state.WriteStateAsync();
        }
    }

    public async Task SetItemPriceAsync(Guid tabId, Guid itemId, string? price)
    {
        if (_state.State.Tabs.TryGetValue(tabId, out var tab))
        {
            if (price != null)
                tab.ItemPrices[itemId] = price;
            else
                tab.ItemPrices.Remove(itemId);
            await _state.WriteStateAsync();
        }
    }

    public async Task SetTabDefaultPriceAsync(Guid tabId, string? price)
    {
        if (_state.State.Tabs.TryGetValue(tabId, out var tab))
        {
            tab.Metadata = tab.Metadata with { DefaultPrice = price };
            await _state.WriteStateAsync();
        }
    }

    #endregion

    private async Task<BaseType?> GetBaseTypeAsync(string baseTypeId)
    {
        var reader = _grainFactory.GetGrain<IBaseTypeReaderGrain>("default");
        return await reader.GetAsync(baseTypeId);
    }
}
