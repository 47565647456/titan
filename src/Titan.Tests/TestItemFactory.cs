using Titan.Abstractions.Models.Items;

namespace Titan.Tests;

/// <summary>
/// Factory for creating test items with various configurations.
/// </summary>
public static class TestItemFactory
{
    /// <summary>
    /// Creates a simple normal item with the given base type.
    /// </summary>
    public static Item CreateNormalItem(string baseTypeId, int itemLevel = 1)
    {
        return new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = itemLevel,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a magic item with specified number of prefixes and suffixes.
    /// </summary>
    public static Item CreateMagicItem(string baseTypeId, int itemLevel = 10, int prefixCount = 1, int suffixCount = 0)
    {
        var prefixes = Enumerable.Range(0, prefixCount)
            .Select(i => CreateTestModifier($"test_prefix_{i}", $"+{(i + 1) * 10} to Test Stat"))
            .ToList();

        var suffixes = Enumerable.Range(0, suffixCount)
            .Select(i => CreateTestModifier($"test_suffix_{i}", $"+{(i + 1) * 5}% Test Resistance"))
            .ToList();

        return new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = itemLevel,
            Rarity = ItemRarity.Magic,
            Prefixes = prefixes,
            Suffixes = suffixes,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a rare item with 4-6 modifiers.
    /// </summary>
    public static Item CreateRareItem(string baseTypeId, int itemLevel = 20)
    {
        return new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = itemLevel,
            Rarity = ItemRarity.Rare,
            Name = "Test Rare Item",
            Prefixes = new List<RolledModifier>
            {
                CreateTestModifier("added_physical_1", "+5 to 10 Physical Damage", new[] { 5, 10 }),
                CreateTestModifier("increased_life_1", "+15 to Maximum Life", new[] { 15 })
            },
            Suffixes = new List<RolledModifier>
            {
                CreateTestModifier("fire_resistance_1", "+15% to Fire Resistance", new[] { 15 }),
                CreateTestModifier("attack_speed_1", "8% Increased Attack Speed", new[] { 8 })
            },
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a test sword with 1x3 dimensions.
    /// </summary>
    public static Item CreateTestSword(int itemLevel = 1)
    {
        return CreateNormalItem("simple_sword", itemLevel);
    }

    /// <summary>
    /// Creates a test armour (leather vest) with 2x3 dimensions.
    /// </summary>
    public static Item CreateTestArmour(int itemLevel = 1)
    {
        return CreateNormalItem("leather_vest", itemLevel);
    }

    /// <summary>
    /// Creates a test ring with 1x1 dimensions.
    /// </summary>
    public static Item CreateTestRing(int itemLevel = 1)
    {
        return CreateNormalItem("gold_ring", itemLevel);
    }

    /// <summary>
    /// Creates a test belt with 2x1 dimensions.
    /// </summary>
    public static Item CreateTestBelt(int itemLevel = 1)
    {
        return CreateNormalItem("leather_belt", itemLevel);
    }

    /// <summary>
    /// Creates a stackable currency item.
    /// </summary>
    public static Item CreateCurrency(int quantity = 1)
    {
        return new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = "gold_coin",
            ItemLevel = 1,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a test base type definition.
    /// </summary>
    public static BaseType CreateTestBaseType(
        string id, 
        int width = 1, 
        int height = 1, 
        EquipmentSlot slot = EquipmentSlot.None,
        ItemCategory category = ItemCategory.Equipment)
    {
        return new BaseType
        {
            BaseTypeId = id,
            Name = $"Test {id}",
            Description = $"A test item: {id}",
            Category = category,
            Slot = slot,
            Width = width,
            Height = height,
            RequiredLevel = 1,
            MaxSockets = slot == EquipmentSlot.None ? 0 : 4,
            IsTradeable = true,
            Tags = new HashSet<string> { "test" }
        };
    }

    /// <summary>
    /// Creates a test modifier definition.
    /// </summary>
    public static ModifierDefinition CreateTestModifierDefinition(
        string id,
        ModifierType type = ModifierType.Prefix,
        int tier = 1,
        int minValue = 1,
        int maxValue = 10)
    {
        return new ModifierDefinition
        {
            ModifierId = id,
            DisplayTemplate = $"+{{0}} Test Stat ({id})",
            Type = type,
            Tier = tier,
            RequiredItemLevel = 1,
            Ranges = new[] { new ModifierRange { Min = minValue, Max = maxValue } },
            Weight = 1000,
            ModifierGroup = $"test_{id}_group"
        };
    }

    /// <summary>
    /// Creates a rolled modifier for item creation.
    /// </summary>
    public static RolledModifier CreateTestModifier(string modifierId, string displayText, int[]? values = null)
    {
        return new RolledModifier
        {
            ModifierId = modifierId,
            Values = values ?? new[] { 5 },
            DisplayText = displayText
        };
    }

    /// <summary>
    /// Creates an item with sockets.
    /// </summary>
    public static Item CreateItemWithSockets(string baseTypeId, int socketCount, int linkGroups = 1)
    {
        var sockets = new List<Socket>();
        var colors = new[] { SocketColor.Red, SocketColor.Green, SocketColor.Blue };
        
        for (int i = 0; i < socketCount; i++)
        {
            sockets.Add(new Socket
            {
                Group = i % linkGroups,
                Color = colors[i % colors.Length]
            });
        }

        return new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 20,
            Rarity = ItemRarity.Normal,
            Sockets = sockets,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
