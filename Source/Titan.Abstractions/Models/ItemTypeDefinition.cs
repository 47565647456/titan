using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Defines an item type in the game's item catalogue.
/// Used by the ItemTypeRegistry to validate and configure item behavior.
/// </summary>
[GenerateSerializer]
[Alias("ItemTypeDefinition")]
public record ItemTypeDefinition
{
    /// <summary>
    /// Unique identifier for this item type (e.g., "sword_legendary", "potion_health").
    /// </summary>
    [Id(0)] public required string ItemTypeId { get; init; }

    /// <summary>
    /// Display name for this item type.
    /// </summary>
    [Id(1)] public required string Name { get; init; }

    /// <summary>
    /// Optional description of the item type.
    /// </summary>
    [Id(2)] public string? Description { get; init; }

    /// <summary>
    /// Maximum stack size for this item type.
    /// 1 = non-stackable (e.g., legendary sword), higher = stackable (e.g., potions).
    /// </summary>
    [Id(3)] public int MaxStackSize { get; init; } = 1;

    /// <summary>
    /// Whether this item type can be traded between players.
    /// Set to false for soulbound/account-bound items.
    /// </summary>
    [Id(4)] public bool IsTradeable { get; init; } = true;

    /// <summary>
    /// Optional category for organization (e.g., "weapon", "consumable", "material").
    /// </summary>
    [Id(5)] public string? Category { get; init; }
}
