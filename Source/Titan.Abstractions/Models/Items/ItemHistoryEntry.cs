using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models.Items;

/// <summary>
/// Records a lifecycle event for an item.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("ItemHistoryEntry")]
public partial record ItemHistoryEntry
{
    /// <summary>
    /// Unique identifier for this event.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required Guid EventId { get; init; }

    /// <summary>
    /// The item this event relates to.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required Guid ItemId { get; init; }

    /// <summary>
    /// Type of event (e.g., "Generated", "Traded", "Crafted").
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public required string EventType { get; init; }

    /// <summary>
    /// When this event occurred.
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Account that performed this action (if applicable).
    /// </summary>
    [Id(4), MemoryPackOrder(4)] public Guid? ActorAccountId { get; init; }

    /// <summary>
    /// Character that performed this action (if applicable).
    /// </summary>
    [Id(5), MemoryPackOrder(5)] public Guid? ActorCharacterId { get; init; }

    /// <summary>
    /// Additional details about the event.
    /// </summary>
    [Id(6), MemoryPackOrder(6)] public Dictionary<string, string>? Details { get; init; }
}

/// <summary>
/// Constants for item event types.
/// </summary>
public static class ItemEventTypes
{
    /// <summary>Item was generated/dropped.</summary>
    public const string Generated = "Generated";

    /// <summary>Item was crafted/modified.</summary>
    public const string Crafted = "Crafted";

    /// <summary>Item was traded to another character.</summary>
    public const string Traded = "Traded";

    /// <summary>Item was dropped/destroyed.</summary>
    public const string Dropped = "Dropped";

    /// <summary>Item was equipped.</summary>
    public const string Equipped = "Equipped";

    /// <summary>Item was unequipped.</summary>
    public const string Unequipped = "Unequipped";

    /// <summary>Item sockets were modified.</summary>
    public const string SocketModified = "SocketModified";

    /// <summary>Item was corrupted.</summary>
    public const string Corrupted = "Corrupted";

    /// <summary>Item was destroyed/deleted.</summary>
    public const string Destroyed = "Destroyed";

    /// <summary>Item was moved in inventory.</summary>
    public const string Moved = "Moved";

    /// <summary>Item was stashed.</summary>
    public const string Stashed = "Stashed";

    /// <summary>Item was retrieved from stash.</summary>
    public const string Retrieved = "Retrieved";
}
