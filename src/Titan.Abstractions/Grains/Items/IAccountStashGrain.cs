using Orleans;
using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Grains.Items;

/// <summary>
/// Per-account stash grain with multiple tabs.
/// Key: AccountId
/// </summary>
public interface IAccountStashGrain : IGrainWithGuidKey
{
    #region Tab Management

    /// <summary>
    /// Gets all stash tabs.
    /// </summary>
    Task<IReadOnlyList<StashTab>> GetTabsAsync();

    /// <summary>
    /// Gets a specific tab.
    /// </summary>
    Task<StashTab?> GetTabAsync(Guid tabId);

    /// <summary>
    /// Creates a new stash tab.
    /// </summary>
    Task<StashTab> CreateTabAsync(string name, StashTabType type = StashTabType.General, 
                                   int gridWidth = 12, int gridHeight = 12);

    /// <summary>
    /// Renames a tab.
    /// </summary>
    Task<StashTab?> RenameTabAsync(Guid tabId, string newName);

    /// <summary>
    /// Deletes a tab and all its items.
    /// </summary>
    Task<bool> DeleteTabAsync(Guid tabId);

    /// <summary>
    /// Reorders tabs.
    /// </summary>
    Task ReorderTabsAsync(Guid[] tabIdsInOrder);

    /// <summary>
    /// Sets the affinity for a tab (for quick-deposit).
    /// </summary>
    Task SetTabAffinityAsync(Guid tabId, ItemCategory? affinity);

    #endregion

    #region Grid Operations

    /// <summary>
    /// Gets the grid state for a tab.
    /// </summary>
    Task<InventoryGrid?> GetTabGridAsync(Guid tabId);

    /// <summary>
    /// Gets all items in a tab.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Item>> GetTabItemsAsync(Guid tabId);

    /// <summary>
    /// Deposits an item at a specific position.
    /// </summary>
    Task<bool> DepositAsync(Guid tabId, Item item, int x, int y);

    /// <summary>
    /// Deposits an item at the first available position.
    /// </summary>
    Task<(int X, int Y)?> DepositAutoAsync(Guid tabId, Item item);

    /// <summary>
    /// Quick deposit using affinities - finds the correct tab automatically.
    /// Returns the tab ID and position, or null if no space.
    /// </summary>
    Task<(Guid TabId, int X, int Y)?> QuickDepositAsync(Item item, ItemCategory category);

    /// <summary>
    /// Withdraws an item from a tab.
    /// </summary>
    Task<Item?> WithdrawAsync(Guid tabId, Guid itemId);

    /// <summary>
    /// Moves an item within a tab.
    /// </summary>
    Task<bool> MoveItemAsync(Guid tabId, Guid itemId, int newX, int newY);

    /// <summary>
    /// Moves an item between tabs.
    /// </summary>
    Task<bool> MoveItemBetweenTabsAsync(Guid fromTabId, Guid toTabId, Guid itemId, int x, int y);

    #endregion

    #region Trading

    /// <summary>
    /// Sets a tab as public for trading (Premium tabs only).
    /// </summary>
    Task SetTabPublicAsync(Guid tabId, bool isPublic);

    /// <summary>
    /// Sets the price for an item in a public tab.
    /// </summary>
    Task SetItemPriceAsync(Guid tabId, Guid itemId, string? price);

    /// <summary>
    /// Sets the default price for all items in a tab.
    /// </summary>
    Task SetTabDefaultPriceAsync(Guid tabId, string? price);

    #endregion
}
