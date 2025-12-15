using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models.Items;

/// <summary>
/// Defines a unique item template with fixed modifiers.
/// Unique items have predetermined modifiers with variable value ranges.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("UniqueDefinition")]
public partial record UniqueDefinition
{
    /// <summary>
    /// Unique identifier (e.g., "kaoms_heart").
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required string UniqueId { get; init; }

    /// <summary>
    /// Display name (localization key, e.g., "unique.kaoms_heart").
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required string Name { get; init; }

    /// <summary>
    /// Base type this unique is built on.
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public required string BaseTypeId { get; init; }

    /// <summary>
    /// Flavour text lines (localization keys).
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public string[]? FlavourText { get; init; }

    /// <summary>
    /// Relative drop weight. Lower = rarer.
    /// </summary>
    [Id(4), MemoryPackOrder(4)] public int DropWeight { get; init; } = 100;

    /// <summary>
    /// Minimum item level for this unique to drop.
    /// </summary>
    [Id(5), MemoryPackOrder(5)] public int RequiredItemLevel { get; init; } = 1;

    /// <summary>
    /// Fixed modifiers on this unique item.
    /// </summary>
    [Id(6), MemoryPackOrder(6)] public required UniqueModifier[] Modifiers { get; init; }

    /// <summary>
    /// Special properties (e.g., "no_sockets": "true").
    /// </summary>
    [Id(7), MemoryPackOrder(7)] public Dictionary<string, string> SpecialProperties { get; init; } = new();
}

/// <summary>
/// A modifier on a unique item with its value ranges.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record UniqueModifier
{
    /// <summary>
    /// Display text (localization key with placeholders).
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required string DisplayText { get; init; }

    /// <summary>
    /// Value ranges for placeholders in display text.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required ModifierRange[] Ranges { get; init; }
}
