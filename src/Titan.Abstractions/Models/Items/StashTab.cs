using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models.Items;

/// <summary>
/// A stash tab in the account-wide storage.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("StashTab")]
public partial record StashTab
{
    /// <summary>
    /// Unique identifier for this tab.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required Guid TabId { get; init; }

    /// <summary>
    /// Display name of the tab.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required string Name { get; init; }

    /// <summary>
    /// Type of stash tab (affects functionality).
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public StashTabType Type { get; init; } = StashTabType.General;

    /// <summary>
    /// Sort order for display.
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public int SortOrder { get; init; }

    /// <summary>
    /// Grid width for this tab.
    /// </summary>
    [Id(4), MemoryPackOrder(4)] public int GridWidth { get; init; } = 12;

    /// <summary>
    /// Grid height for this tab.
    /// </summary>
    [Id(5), MemoryPackOrder(5)] public int GridHeight { get; init; } = 12;

    /// <summary>
    /// Item category affinity for quick-deposit.
    /// Items of this category will be auto-deposited here.
    /// </summary>
    [Id(6), MemoryPackOrder(6)] public ItemCategory? Affinity { get; init; }

    /// <summary>
    /// Whether this tab is public for trading (Premium tabs only).
    /// </summary>
    [Id(7), MemoryPackOrder(7)] public bool IsPublic { get; init; }

    /// <summary>
    /// Default price for all items in this tab (for trading).
    /// </summary>
    [Id(8), MemoryPackOrder(8)] public string? DefaultPrice { get; init; }
}
