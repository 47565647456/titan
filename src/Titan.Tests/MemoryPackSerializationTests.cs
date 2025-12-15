using MemoryPack;
using Titan.Abstractions.Events;
using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;
using Titan.Grains.Items;
using Titan.ServiceDefaults.Serialization;

namespace Titan.Tests;

/// <summary>
/// Tests validating MemoryPack serialization roundtrip for all model types.
/// Ensures data integrity is preserved through serialization/deserialization.
/// </summary>
public class MemoryPackSerializationTests
{
    #region New Item System Models

    [Fact]
    public void Item_Roundtrip_PreservesAllData()
    {
        var original = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = "legendary_sword",
            ItemLevel = 75,
            Rarity = ItemRarity.Rare,
            Name = "Doom Blade",
            Implicit = new RolledModifier { ModifierId = "implicit_crit", Values = new[] { 15 }, DisplayText = "+15% Critical Chance" },
            Prefixes = new List<RolledModifier>
            {
                new RolledModifier { ModifierId = "mod_fire", Values = new[] { 100, 150 }, DisplayText = "+100-150 Fire Damage" }
            },
            Suffixes = new List<RolledModifier>
            {
                new RolledModifier { ModifierId = "mod_attack_speed", Values = new[] { 15 }, DisplayText = "+15% Attack Speed" }
            },
            Sockets = new List<Socket>
            {
                new Socket { Group = 0, Color = SocketColor.Red },
                new Socket { Group = 0, Color = SocketColor.Green },
                new Socket { Group = 1, Color = SocketColor.Blue }
            },
            IsIdentified = true,
            IsCorrupted = false,
            IsMirrored = false,
            Quantity = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<Item>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.BaseTypeId, deserialized.BaseTypeId);
        Assert.Equal(original.ItemLevel, deserialized.ItemLevel);
        Assert.Equal(original.Rarity, deserialized.Rarity);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.NotNull(deserialized.Implicit);
        Assert.Equal(original.Implicit.ModifierId, deserialized.Implicit!.ModifierId);
        Assert.Equal(original.Prefixes.Count, deserialized.Prefixes.Count);
        Assert.Equal(original.Suffixes.Count, deserialized.Suffixes.Count);
        Assert.Equal(original.Sockets.Count, deserialized.Sockets.Count);
        Assert.Equal(original.IsIdentified, deserialized.IsIdentified);
        Assert.Equal(original.IsCorrupted, deserialized.IsCorrupted);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
    }

    [Fact]
    public void BaseType_Roundtrip_PreservesAllData()
    {
        var original = new BaseType
        {
            BaseTypeId = "legendary_sword",
            Name = "Legendary Sword",
            Description = "A powerful weapon",
            Category = ItemCategory.Equipment,
            Slot = EquipmentSlot.MainHand,
            Width = 1,
            Height = 3,
            RequiredLevel = 50,
            RequiredStrength = 100,
            RequiredDexterity = 50,
            RequiredIntelligence = 0,
            MaxSockets = 6,
            ImplicitModifierId = "implicit_physical_damage",
            Tags = new HashSet<string> { "weapon", "sword", "two_handed" },
            MaxStackSize = 1,
            IsTradeable = true,
            BaseStats = new Dictionary<string, int> { ["physical_damage"] = 100 }
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<BaseType>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.BaseTypeId, deserialized.BaseTypeId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Category, deserialized.Category);
        Assert.Equal(original.Slot, deserialized.Slot);
        Assert.Equal(original.Width, deserialized.Width);
        Assert.Equal(original.Height, deserialized.Height);
        Assert.Equal(original.RequiredLevel, deserialized.RequiredLevel);
        Assert.Equal(original.RequiredStrength, deserialized.RequiredStrength);
        Assert.Equal(original.MaxSockets, deserialized.MaxSockets);
        Assert.Equal(original.ImplicitModifierId, deserialized.ImplicitModifierId);
        Assert.Equal(original.Tags.Count, deserialized.Tags.Count);
        Assert.Equal(original.IsTradeable, deserialized.IsTradeable);
    }

    [Fact]
    public void ModifierDefinition_Roundtrip_PreservesAllData()
    {
        var original = new ModifierDefinition
        {
            ModifierId = "mod_fire_damage",
            DisplayTemplate = "+{0} to {1} Fire Damage",
            Type = ModifierType.Prefix,
            ModifierGroup = "flat_fire_damage",
            Tier = 1,
            RequiredItemLevel = 50,
            Ranges = new[]
            {
                new ModifierRange { Min = 100, Max = 150 },
                new ModifierRange { Min = 150, Max = 200 }
            },
            Weight = 1000,
            RequiredTags = new HashSet<string> { "weapon" },
            ExcludedTags = new HashSet<string> { "bow" }
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<ModifierDefinition>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ModifierId, deserialized.ModifierId);
        Assert.Equal(original.DisplayTemplate, deserialized.DisplayTemplate);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Tier, deserialized.Tier);
        Assert.Equal(original.RequiredItemLevel, deserialized.RequiredItemLevel);
        Assert.Equal(original.Ranges.Length, deserialized.Ranges.Length);
        Assert.Equal(original.Weight, deserialized.Weight);
    }

    [Fact]
    public void InventoryGrid_Roundtrip_PreservesData()
    {
        var original = InventoryGrid.Create(12, 5);
        
        // Simulate placing an item
        var itemId = Guid.NewGuid();
        original.Cells[0][0] = itemId;
        original.Cells[0][1] = itemId;
        original.Cells[1][0] = itemId;
        original.Cells[1][1] = itemId;
        original.Placements[itemId] = new GridPlacement { ItemId = itemId, X = 0, Y = 0 };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<InventoryGrid>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Width, deserialized.Width);
        Assert.Equal(original.Height, deserialized.Height);
        Assert.Equal(original.Cells.Length, deserialized.Cells.Length);
        Assert.Equal(original.Placements.Count, deserialized.Placements.Count);
        Assert.Equal(itemId, deserialized.Cells[0][0]);
    }

    [Fact]
    public void CharacterInventoryState_Roundtrip_PreservesData()
    {
        var itemId = Guid.NewGuid();
        var original = new CharacterInventoryState
        {
            Stats = new CharacterStats { Level = 50, Strength = 100, Dexterity = 75, Intelligence = 30 },
            BagGrid = InventoryGrid.Create(12, 5),
            BagItems = new Dictionary<Guid, Item>
            {
                [itemId] = new Item
                {
                    Id = itemId,
                    BaseTypeId = "health_potion",
                    ItemLevel = 1,
                    Rarity = ItemRarity.Normal,
                    Quantity = 5,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            },
            Equipped = new Dictionary<EquipmentSlot, Item>()
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<CharacterInventoryState>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Stats.Level, deserialized.Stats.Level);
        Assert.Equal(original.Stats.Strength, deserialized.Stats.Strength);
        Assert.Equal(original.BagGrid.Width, deserialized.BagGrid.Width);
        Assert.Equal(original.BagItems.Count, deserialized.BagItems.Count);
        Assert.Equal(original.BagItems[itemId].BaseTypeId, deserialized.BagItems[itemId].BaseTypeId);
    }

    #endregion

    #region Trade Models

    [Fact]
    public void TradeSession_Roundtrip_PreservesAllData()
    {
        var original = new TradeSession
        {
            TradeId = Guid.NewGuid(),
            InitiatorCharacterId = Guid.NewGuid(),
            TargetCharacterId = Guid.NewGuid(),
            SeasonId = "season-1",
            Status = TradeStatus.Pending,
            InitiatorItemIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
            TargetItemIds = new List<Guid> { Guid.NewGuid() },
            InitiatorAccepted = true,
            TargetAccepted = false,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = null
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<TradeSession>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.TradeId, deserialized.TradeId);
        Assert.Equal(original.InitiatorCharacterId, deserialized.InitiatorCharacterId);
        Assert.Equal(original.TargetCharacterId, deserialized.TargetCharacterId);
        Assert.Equal(original.SeasonId, deserialized.SeasonId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.InitiatorItemIds.Count, deserialized.InitiatorItemIds.Count);
        Assert.Equal(original.TargetItemIds.Count, deserialized.TargetItemIds.Count);
        Assert.Equal(original.InitiatorAccepted, deserialized.InitiatorAccepted);
        Assert.Equal(original.TargetAccepted, deserialized.TargetAccepted);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
        Assert.Null(deserialized.CompletedAt);
    }

    [Fact]
    public void TradeEvent_Roundtrip_PreservesAllData()
    {
        var session = new TradeSession
        {
            TradeId = Guid.NewGuid(),
            InitiatorCharacterId = Guid.NewGuid(),
            TargetCharacterId = Guid.NewGuid(),
            SeasonId = "standard",
            Status = TradeStatus.Completed
        };

        var original = new TradeEvent
        {
            TradeId = session.TradeId,
            EventType = "TradeCompleted",
            Timestamp = DateTimeOffset.UtcNow,
            Session = session,
            UserId = Guid.NewGuid(),
            ItemId = Guid.NewGuid()
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<TradeEvent>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.TradeId, deserialized.TradeId);
        Assert.Equal(original.EventType, deserialized.EventType);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.NotNull(deserialized.Session);
        Assert.Equal(original.Session.TradeId, deserialized.Session.TradeId);
        Assert.Equal(original.UserId, deserialized.UserId);
        Assert.Equal(original.ItemId, deserialized.ItemId);
    }

    #endregion

    #region Character Models

    [Fact]
    public void Character_Roundtrip_PreservesAllData()
    {
        var original = new Character
        {
            CharacterId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            SeasonId = "season-1",
            Name = "TestCharacter",
            Restrictions = CharacterRestrictions.Hardcore | CharacterRestrictions.SoloSelfFound,
            Level = 50,
            Experience = 125000,
            Stats = new Dictionary<string, int>
            {
                ["strength"] = 100,
                ["dexterity"] = 75
            },
            IsDead = false,
            IsMigrated = false,
            OriginalSeasonId = null,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<Character>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.CharacterId, deserialized.CharacterId);
        Assert.Equal(original.AccountId, deserialized.AccountId);
        Assert.Equal(original.SeasonId, deserialized.SeasonId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Restrictions, deserialized.Restrictions);
        Assert.Equal(original.Level, deserialized.Level);
        Assert.Equal(original.Experience, deserialized.Experience);
        Assert.Equal(original.Stats["strength"], deserialized.Stats["strength"]);
        Assert.Equal(original.Stats["dexterity"], deserialized.Stats["dexterity"]);
        Assert.Equal(original.IsDead, deserialized.IsDead);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
    }

    [Fact]
    public void CharacterHistoryEntry_Roundtrip_PreservesAllData()
    {
        var original = new CharacterHistoryEntry
        {
            EventType = CharacterEventTypes.Created,
            Description = "Character created",
            Timestamp = DateTimeOffset.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["seasonId"] = "standard",
                ["restrictions"] = "Hardcore"
            }
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<CharacterHistoryEntry>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.EventType, deserialized.EventType);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.Data!["seasonId"], deserialized.Data!["seasonId"]);
    }

    #endregion

    #region Account Models

    [Fact]
    public void Account_Roundtrip_PreservesAllData()
    {
        var original = new Account
        {
            AccountId = Guid.NewGuid(),
            UnlockedCosmetics = new List<string> { "skin_gold", "pet_dragon" },
            UnlockedAchievements = new List<string> { "first_kill", "level_50" }
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<Account>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.AccountId, deserialized.AccountId);
        Assert.Equal(2, deserialized.UnlockedCosmetics.Count);
        Assert.Contains("skin_gold", deserialized.UnlockedCosmetics);
        Assert.Equal(2, deserialized.UnlockedAchievements.Count);
    }

    #endregion

    #region Season Models

    [Fact]
    public void Season_Roundtrip_PreservesAllData()
    {
        var original = new Season
        {
            SeasonId = "season-2024-winter",
            Name = "Winter 2024",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow.AddDays(-30),
            EndDate = DateTimeOffset.UtcNow.AddDays(60),
            MigrationTargetId = "standard",
            Modifiers = new Dictionary<string, string>
            {
                ["xp_bonus"] = "1.5",
                ["drop_rate"] = "2.0"
            },
            IsVoid = false
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<Season>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.SeasonId, deserialized.SeasonId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.StartDate, deserialized.StartDate);
        Assert.Equal(original.EndDate, deserialized.EndDate);
        Assert.Equal(original.MigrationTargetId, deserialized.MigrationTargetId);
        Assert.Equal(original.Modifiers!["xp_bonus"], deserialized.Modifiers!["xp_bonus"]);
        Assert.Equal(original.IsVoid, deserialized.IsVoid);
    }

    #endregion

    #region User Models

    [Fact]
    public void UserIdentity_Roundtrip_PreservesAllData()
    {
        var original = new UserIdentity
        {
            UserId = Guid.NewGuid(),
            LinkedProviders = new List<LinkedProvider>
            {
                new LinkedProvider { ProviderName = "Steam", ExternalId = "steam123", LinkedAt = DateTimeOffset.UtcNow },
                new LinkedProvider { ProviderName = "Discord", ExternalId = "discord456", LinkedAt = DateTimeOffset.UtcNow }
            }
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<UserIdentity>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.UserId, deserialized.UserId);
        Assert.Equal(2, deserialized.LinkedProviders.Count);
        Assert.Equal("Steam", deserialized.LinkedProviders[0].ProviderName);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Item_WithMinimalData_Roundtrip_Succeeds()
    {
        var original = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = "basic_sword",
            ItemLevel = 1,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<Item>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.BaseTypeId, deserialized.BaseTypeId);
        Assert.Null(deserialized.Name);
        Assert.Null(deserialized.Implicit);
        Assert.Empty(deserialized.Prefixes);
        Assert.Empty(deserialized.Suffixes);
    }

    [Fact]
    public void TradeSession_WithEmptyItemLists_Roundtrip_Succeeds()
    {
        var original = new TradeSession
        {
            TradeId = Guid.NewGuid(),
            InitiatorCharacterId = Guid.NewGuid(),
            TargetCharacterId = Guid.NewGuid(),
            SeasonId = "standard",
            Status = TradeStatus.Pending,
            InitiatorItemIds = new List<Guid>(),
            TargetItemIds = new List<Guid>()
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<TradeSession>(bytes);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.InitiatorItemIds);
        Assert.Empty(deserialized.TargetItemIds);
    }

    #endregion

    #region Storage Serializer

    [Fact]
    public void GrainStorageSerializer_Serialize_ReturnsValidBinaryData()
    {
        var serializer = new MemoryPackGrainStorageSerializer();
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = "test_item",
            ItemLevel = 10,
            Rarity = ItemRarity.Magic,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var binaryData = serializer.Serialize(item);

        Assert.NotNull(binaryData);
        Assert.True(binaryData.ToArray().Length > 0);
    }

    [Fact]
    public void GrainStorageSerializer_Roundtrip_PreservesData()
    {
        var serializer = new MemoryPackGrainStorageSerializer();
        var original = new TradeSession
        {
            TradeId = Guid.NewGuid(),
            InitiatorCharacterId = Guid.NewGuid(),
            TargetCharacterId = Guid.NewGuid(),
            SeasonId = "test-season",
            Status = TradeStatus.Pending,
            InitiatorItemIds = [Guid.NewGuid()],
            TargetItemIds = [],
            CreatedAt = DateTimeOffset.UtcNow
        };

        var binaryData = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<TradeSession>(binaryData);

        Assert.Equal(original.TradeId, deserialized.TradeId);
        Assert.Equal(original.SeasonId, deserialized.SeasonId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Single(original.InitiatorItemIds);
        Assert.Empty(original.TargetItemIds);
    }

    #endregion
}
