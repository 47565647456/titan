using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models.Items;

/// <summary>
/// Defines a base type template for items.
/// Base types define the fundamental properties of an item before any modifiers.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("BaseType")]
public partial record BaseType
{
    /// <summary>
    /// Unique identifier for this base type (e.g., "vaal_axe", "astral_plate").
    /// Also serves as the localization key for the name.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required string BaseTypeId { get; init; }

    /// <summary>
    /// Display name (localization key, e.g., "item.base.vaal_axe").
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required string Name { get; init; }

    /// <summary>
    /// Optional description (localization key).
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public string? Description { get; init; }

    /// <summary>
    /// Item category for filtering and stash affinities.
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public ItemCategory Category { get; init; }

    /// <summary>
    /// Equipment slot this item can be equipped to.
    /// None for non-equippable items (currency, materials, etc.).
    /// </summary>
    [Id(4), MemoryPackOrder(4)] public EquipmentSlot Slot { get; init; }

    /// <summary>
    /// Width in inventory grid cells.
    /// </summary>
    [Id(5), MemoryPackOrder(5)] public int Width { get; init; } = 1;

    /// <summary>
    /// Height in inventory grid cells.
    /// </summary>
    [Id(6), MemoryPackOrder(6)] public int Height { get; init; } = 1;

    /// <summary>
    /// Required character level to equip.
    /// </summary>
    [Id(7), MemoryPackOrder(7)] public int RequiredLevel { get; init; }

    /// <summary>
    /// Required Strength to equip.
    /// </summary>
    [Id(8), MemoryPackOrder(8)] public int RequiredStrength { get; init; }

    /// <summary>
    /// Required Dexterity to equip.
    /// </summary>
    [Id(9), MemoryPackOrder(9)] public int RequiredDexterity { get; init; }

    /// <summary>
    /// Required Intelligence to equip.
    /// </summary>
    [Id(10), MemoryPackOrder(10)] public int RequiredIntelligence { get; init; }

    /// <summary>
    /// Maximum number of sockets this base type can have.
    /// 0 for non-socketable items.
    /// </summary>
    [Id(11), MemoryPackOrder(11)] public int MaxSockets { get; init; }

    /// <summary>
    /// Maximum stack size. 1 for non-stackable items (equipment).
    /// Higher values for currency, materials, etc.
    /// </summary>
    [Id(12), MemoryPackOrder(12)] public int MaxStackSize { get; init; } = 1;

    /// <summary>
    /// Whether items of this type can be traded between players.
    /// </summary>
    [Id(13), MemoryPackOrder(13)] public bool IsTradeable { get; init; } = true;

    /// <summary>
    /// ID of the implicit modifier built into this base type.
    /// </summary>
    [Id(14), MemoryPackOrder(14)] public string? ImplicitModifierId { get; init; }

    /// <summary>
    /// Tags used for modifier filtering (e.g., "weapon", "axe", "two_hand").
    /// </summary>
    [Id(15), MemoryPackOrder(15)] public HashSet<string> Tags { get; init; } = new();

    /// <summary>
    /// Base stats for this item type (e.g., "physical_damage_min": 10).
    /// </summary>
    [Id(16), MemoryPackOrder(16)] public Dictionary<string, int> BaseStats { get; init; } = new();
}
