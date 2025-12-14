using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models.Items;

/// <summary>
/// Defines a modifier that can roll on items.
/// Modifiers are templates with value ranges; actual values are rolled when applied to items.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("ModifierDefinition")]
public partial record ModifierDefinition
{
    /// <summary>
    /// Unique identifier for this modifier (e.g., "added_physical_damage_1").
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required string ModifierId { get; init; }

    /// <summary>
    /// Display template (localization key with placeholders).
    /// Example: "mod.added_physical_damage" -> "Adds {0} to {1} Physical Damage"
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required string DisplayTemplate { get; init; }

    /// <summary>
    /// Type of modifier (Implicit, Prefix, Suffix, etc.).
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public ModifierType Type { get; init; }

    /// <summary>
    /// Tier of this modifier. 1 = best tier, higher numbers = weaker tiers.
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public int Tier { get; init; } = 1;

    /// <summary>
    /// Minimum item level required to roll this modifier.
    /// </summary>
    [Id(4), MemoryPackOrder(4)] public int RequiredItemLevel { get; init; } = 1;

    /// <summary>
    /// Value ranges for each placeholder in DisplayTemplate.
    /// </summary>
    [Id(5), MemoryPackOrder(5)] public required ModifierRange[] Ranges { get; init; }

    /// <summary>
    /// Relative spawn weight. Higher = more common.
    /// </summary>
    [Id(6), MemoryPackOrder(6)] public int Weight { get; init; } = 1000;

    /// <summary>
    /// Tags the item must have to roll this modifier.
    /// </summary>
    [Id(7), MemoryPackOrder(7)] public HashSet<string> RequiredTags { get; init; } = new();

    /// <summary>
    /// Tags the item must NOT have to roll this modifier.
    /// </summary>
    [Id(8), MemoryPackOrder(8)] public HashSet<string> ExcludedTags { get; init; } = new();

    /// <summary>
    /// Modifier group. Only one modifier from each group can appear on an item.
    /// Example: "life", "fire_resistance"
    /// </summary>
    [Id(9), MemoryPackOrder(9)] public string? ModifierGroup { get; init; }
}

/// <summary>
/// Defines a range for modifier value rolling.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record ModifierRange
{
    [Id(0), MemoryPackOrder(0)] public int Min { get; init; }
    [Id(1), MemoryPackOrder(1)] public int Max { get; init; }
}
