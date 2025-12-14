using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Helpers;

/// <summary>
/// Helper methods for validating equipment requirements.
/// </summary>
public static class EquipmentValidator
{
    /// <summary>
    /// Checks if a character can equip an item based on its requirements.
    /// </summary>
    /// <param name="stats">Character's current stats.</param>
    /// <param name="baseType">Base type of the item to equip.</param>
    /// <returns>Result indicating success or failure with reasons.</returns>
    public static RequirementCheckResult CanEquip(CharacterStats stats, BaseType baseType)
    {
        var failures = new List<string>();

        // Check if item is equippable
        if (baseType.Slot == EquipmentSlot.None)
        {
            failures.Add("This item cannot be equipped");
            return new RequirementCheckResult { CanEquip = false, FailedRequirements = failures };
        }

        // Level requirement
        if (stats.Level < baseType.RequiredLevel)
        {
            failures.Add($"Requires Level {baseType.RequiredLevel} (you have {stats.Level})");
        }

        // Strength requirement
        if (stats.Strength < baseType.RequiredStrength)
        {
            failures.Add($"Requires {baseType.RequiredStrength} Str (you have {stats.Strength})");
        }

        // Dexterity requirement
        if (stats.Dexterity < baseType.RequiredDexterity)
        {
            failures.Add($"Requires {baseType.RequiredDexterity} Dex (you have {stats.Dexterity})");
        }

        // Intelligence requirement
        if (stats.Intelligence < baseType.RequiredIntelligence)
        {
            failures.Add($"Requires {baseType.RequiredIntelligence} Int (you have {stats.Intelligence})");
        }

        return failures.Count == 0
            ? RequirementCheckResult.Success
            : new RequirementCheckResult { CanEquip = false, FailedRequirements = failures };
    }

    /// <summary>
    /// Validates that an item matches the target equipment slot.
    /// </summary>
    /// <param name="baseType">Base type of the item.</param>
    /// <param name="targetSlot">Target equipment slot.</param>
    /// <returns>True if the item can go in the target slot.</returns>
    public static bool IsSlotValid(BaseType baseType, EquipmentSlot targetSlot)
    {
        // Exact match
        if (baseType.Slot == targetSlot)
            return true;

        // Ring can go in either ring slot
        if (baseType.Slot == EquipmentSlot.RingLeft && targetSlot == EquipmentSlot.RingRight)
            return true;
        if (baseType.Slot == EquipmentSlot.RingRight && targetSlot == EquipmentSlot.RingLeft)
            return true;

        // Weapons can swap between MainHand and OffHand (if not two-handed)
        // This is a simplification - in a full implementation you'd check weapon type
        if ((baseType.Slot == EquipmentSlot.MainHand || baseType.Slot == EquipmentSlot.OffHand) &&
            (targetSlot == EquipmentSlot.MainHand || targetSlot == EquipmentSlot.OffHand))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds equipment slots that no longer meet requirements after stats change.
    /// Used when unequipping items that provide stats.
    /// </summary>
    /// <param name="stats">Updated character stats.</param>
    /// <param name="equipped">Currently equipped items with their base types.</param>
    /// <returns>List of slots containing items that no longer meet requirements.</returns>
    public static List<EquipmentSlot> GetDisabledSlots(
        CharacterStats stats,
        IReadOnlyDictionary<EquipmentSlot, (Item Item, BaseType BaseType)> equipped)
    {
        var disabled = new List<EquipmentSlot>();

        foreach (var (slot, equipment) in equipped)
        {
            var check = CanEquip(stats, equipment.BaseType);
            if (!check.CanEquip)
            {
                disabled.Add(slot);
            }
        }

        return disabled;
    }

    /// <summary>
    /// Calculates effective stats including bonuses from attributes.
    /// Based on PoE formulas:
    /// - +5 Life per 10 STR
    /// - +2% Melee Physical Damage per 10 STR
    /// - +20 Accuracy per 10 DEX
    /// - +2% Evasion per 10 DEX
    /// - +5 Mana per 10 INT
    /// - +2% Energy Shield per 10 INT
    /// </summary>
    public static (int BonusLife, int BonusMana, int BonusAccuracy) CalculateAttributeBonuses(CharacterStats stats)
    {
        int bonusLife = (stats.Strength / 10) * 5;
        int bonusMana = (stats.Intelligence / 10) * 5;
        int bonusAccuracy = (stats.Dexterity / 10) * 20;

        return (bonusLife, bonusMana, bonusAccuracy);
    }
}
