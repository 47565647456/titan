namespace Titan.Abstractions.Models.Items;

/// <summary>
/// Item rarity tier, matching PoE frameType values.
/// </summary>
public enum ItemRarity
{
    Normal = 0,   // White - 0 explicit mods
    Magic = 1,    // Blue - 1-2 explicit mods
    Rare = 2,     // Yellow - 3-6 explicit mods
    Unique = 3    // Orange - fixed mods
}

/// <summary>
/// Types of modifiers that can appear on items.
/// </summary>
public enum ModifierType
{
    Implicit,     // Built into base type
    Prefix,       // Explicit - max 3
    Suffix,       // Explicit - max 3
    Crafted,      // Benchcraft mods
    Enchant,      // Lab/ritual enchants
    Fractured     // Locked mods (cannot be changed)
}

/// <summary>
/// Socket colors based on attribute alignment.
/// </summary>
public enum SocketColor
{
    Red,          // Strength
    Green,        // Dexterity
    Blue,         // Intelligence
    White         // Any (rare)
}

/// <summary>
/// Equipment slots where items can be equipped.
/// </summary>
public enum EquipmentSlot
{
    None,         // Non-equippable
    MainHand,
    OffHand,
    Helmet,
    BodyArmour,
    Gloves,
    Boots,
    Belt,
    Amulet,
    RingLeft,
    RingRight
}

/// <summary>
/// Categories for organizing item types.
/// </summary>
public enum ItemCategory
{
    Currency,
    Equipment,
    Gem,
    Map,
    Consumable,
    Material,
    Quest
}

/// <summary>
/// Types of stash tabs with different functionality.
/// </summary>
public enum StashTabType
{
    General,      // Any items
    Currency,     // Specialized currency storage
    Map,          // Map storage
    Unique,       // Unique collection
    Premium       // Can be made public for trading
}
