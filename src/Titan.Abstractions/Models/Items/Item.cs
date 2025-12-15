using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models.Items;

/// <summary>
/// An item instance in a player's inventory or stash.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("Item")]
public partial record Item
{
    /// <summary>
    /// Unique instance identifier.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required Guid Id { get; init; }

    /// <summary>
    /// Base type this item is built from.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required string BaseTypeId { get; init; }

    /// <summary>
    /// Item level - determines which modifier tiers can roll.
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public int ItemLevel { get; init; } = 1;

    /// <summary>
    /// Rarity tier (Normal, Magic, Rare, Unique).
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public ItemRarity Rarity { get; init; } = ItemRarity.Normal;

    /// <summary>
    /// For rare items: generated name (e.g., "Doom Bane").
    /// For normal/magic: null (client generates from base type).
    /// </summary>
    [Id(4), MemoryPackOrder(4)] public string? Name { get; init; }

    /// <summary>
    /// For unique items: references UniqueDefinition.UniqueId.
    /// </summary>
    [Id(5), MemoryPackOrder(5)] public string? UniqueId { get; init; }

    /// <summary>
    /// Implicit modifier from the base type.
    /// </summary>
    [Id(6), MemoryPackOrder(6)] public RolledModifier? Implicit { get; init; }

    /// <summary>
    /// Prefix modifiers (max 3 for rare items).
    /// </summary>
    [Id(7), MemoryPackOrder(7)] public List<RolledModifier> Prefixes { get; init; } = new();

    /// <summary>
    /// Suffix modifiers (max 3 for rare items).
    /// </summary>
    [Id(8), MemoryPackOrder(8)] public List<RolledModifier> Suffixes { get; init; } = new();

    /// <summary>
    /// Crafted modifiers (from crafting bench).
    /// </summary>
    [Id(9), MemoryPackOrder(9)] public List<RolledModifier> CraftedMods { get; init; } = new();

    /// <summary>
    /// Fractured modifiers (locked, cannot be changed).
    /// </summary>
    [Id(10), MemoryPackOrder(10)] public List<RolledModifier> FracturedMods { get; init; } = new();

    /// <summary>
    /// Socket configuration. Sockets with the same Group are linked.
    /// </summary>
    [Id(11), MemoryPackOrder(11)] public List<Socket> Sockets { get; init; } = new();

    /// <summary>
    /// IDs of gems socketed into this item.
    /// </summary>
    [Id(12), MemoryPackOrder(12)] public List<Guid> SocketedGemIds { get; init; } = new();

    /// <summary>
    /// Stack quantity for stackable items (currency, materials).
    /// </summary>
    [Id(13), MemoryPackOrder(13)] public int Quantity { get; init; } = 1;

    /// <summary>
    /// Whether this item has been identified.
    /// </summary>
    [Id(14), MemoryPackOrder(14)] public bool IsIdentified { get; init; } = true;

    /// <summary>
    /// Whether this item has been corrupted (cannot be modified further).
    /// </summary>
    [Id(15), MemoryPackOrder(15)] public bool IsCorrupted { get; init; }

    /// <summary>
    /// Whether this item is a mirror copy.
    /// </summary>
    [Id(16), MemoryPackOrder(16)] public bool IsMirrored { get; init; }

    /// <summary>
    /// When this item was created.
    /// </summary>
    [Id(17), MemoryPackOrder(17)] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A rolled modifier on an item with actual values.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record RolledModifier
{
    /// <summary>
    /// Reference to ModifierDefinition.ModifierId.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required string ModifierId { get; init; }

    /// <summary>
    /// Rolled values for each placeholder in the display template.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required int[] Values { get; init; }

    /// <summary>
    /// Pre-computed display text (localization key with values substituted).
    /// Client uses this for display.
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public required string DisplayText { get; init; }
}

/// <summary>
/// A socket on an item.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record Socket
{
    /// <summary>
    /// Link group. Sockets with the same group number are linked together.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public int Group { get; init; }

    /// <summary>
    /// Socket color based on attribute alignment.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public SocketColor Color { get; init; }
}
