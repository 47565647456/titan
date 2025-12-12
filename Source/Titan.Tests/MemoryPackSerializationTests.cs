using MemoryPack;
using Titan.Abstractions.Events;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.ServiceDefaults.Serialization;

namespace Titan.Tests;

/// <summary>
/// Tests validating MemoryPack serialization roundtrip for all model types.
/// Ensures data integrity is preserved through serialization/deserialization.
/// </summary>
public class MemoryPackSerializationTests
{
    #region Inventory Models

    [Fact]
    public void Item_Roundtrip_PreservesAllData()
    {
        var original = new Item
        {
            Id = Guid.NewGuid(),
            ItemTypeId = "legendary_sword",
            Quantity = 5,
            Metadata = new Dictionary<string, string>
            {
                ["enchant"] = "fire",
                ["durability"] = "100"
            },
            AcquiredAt = DateTimeOffset.UtcNow
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<Item>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.ItemTypeId, deserialized.ItemTypeId);
        Assert.Equal(original.Quantity, deserialized.Quantity);
        Assert.Equal(original.Metadata!["enchant"], deserialized.Metadata!["enchant"]);
        Assert.Equal(original.Metadata["durability"], deserialized.Metadata["durability"]);
        Assert.Equal(original.AcquiredAt, deserialized.AcquiredAt);
    }

    [Fact]
    public void ItemHistoryEntry_Roundtrip_PreservesAllData()
    {
        var original = new ItemHistoryEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Traded",
            ActorUserId = Guid.NewGuid(),
            TargetUserId = Guid.NewGuid(),
            Details = "Item traded to player"
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<ItemHistoryEntry>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.EventType, deserialized.EventType);
        Assert.Equal(original.ActorUserId, deserialized.ActorUserId);
        Assert.Equal(original.TargetUserId, deserialized.TargetUserId);
        Assert.Equal(original.Details, deserialized.Details);
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

    [Fact]
    public void ChallengeProgress_Roundtrip_PreservesAllData()
    {
        var original = new ChallengeProgress
        {
            ChallengeId = "kill_100_monsters",
            CurrentProgress = 75,
            IsCompleted = false,
            CompletedAt = null
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<ChallengeProgress>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ChallengeId, deserialized.ChallengeId);
        Assert.Equal(original.CurrentProgress, deserialized.CurrentProgress);
        Assert.Equal(original.IsCompleted, deserialized.IsCompleted);
        Assert.Null(deserialized.CompletedAt);
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

    [Fact]
    public void CharacterSummary_Roundtrip_PreservesAllData()
    {
        var original = new CharacterSummary
        {
            CharacterId = Guid.NewGuid(),
            SeasonId = "season-1",
            Name = "TestHero",
            Level = 25,
            Restrictions = CharacterRestrictions.Hardcore,
            IsDead = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7)
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<CharacterSummary>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.CharacterId, deserialized.CharacterId);
        Assert.Equal(original.SeasonId, deserialized.SeasonId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Level, deserialized.Level);
        Assert.Equal(original.Restrictions, deserialized.Restrictions);
        Assert.Equal(original.IsDead, deserialized.IsDead);
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

    [Fact]
    public void SeasonChallenge_Roundtrip_PreservesAllData()
    {
        var original = new SeasonChallenge
        {
            ChallengeId = "c1",
            SeasonId = "season-1",
            Name = "First Blood",
            Description = "Kill 1 enemy",
            RequiredProgress = 1,
            RewardCosmeticId = "badge_first_blood"
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<SeasonChallenge>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ChallengeId, deserialized.ChallengeId);
        Assert.Equal(original.SeasonId, deserialized.SeasonId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.RequiredProgress, deserialized.RequiredProgress);
        Assert.Equal(original.RewardCosmeticId, deserialized.RewardCosmeticId);
    }

    [Fact]
    public void SeasonEvent_Roundtrip_PreservesAllData()
    {
        var original = new SeasonEvent
        {
            SeasonId = "season-1",
            EventType = SeasonEventTypes.SeasonStarted,
            Timestamp = DateTimeOffset.UtcNow,
            Data = "{\"message\": \"Season started!\"}"
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<SeasonEvent>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.SeasonId, deserialized.SeasonId);
        Assert.Equal(original.EventType, deserialized.EventType);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.Data, deserialized.Data);
    }

    [Fact]
    public void MigrationStatus_Roundtrip_PreservesAllData()
    {
        var original = new MigrationStatus
        {
            SourceSeasonId = "season-1",
            TargetSeasonId = "standard",
            State = MigrationState.InProgress,
            TotalCharacters = 100,
            MigratedCharacters = 50,
            FailedCharacters = 2,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = null,
            Errors = new List<string> { "Character 123: timeout", "Character 456: not found" }
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<MigrationStatus>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.SourceSeasonId, deserialized.SourceSeasonId);
        Assert.Equal(original.TargetSeasonId, deserialized.TargetSeasonId);
        Assert.Equal(original.State, deserialized.State);
        Assert.Equal(original.TotalCharacters, deserialized.TotalCharacters);
        Assert.Equal(original.MigratedCharacters, deserialized.MigratedCharacters);
        Assert.Equal(original.FailedCharacters, deserialized.FailedCharacters);
        Assert.Equal(2, deserialized.Errors.Count);
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

    [Fact]
    public void UserProfile_Roundtrip_PreservesAllData()
    {
        var original = new UserProfile
        {
            DisplayName = "TestPlayer",
            AvatarUrl = "https://example.com/avatar.png",
            Settings = new Dictionary<string, string>
            {
                ["theme"] = "dark",
                ["language"] = "en"
            }
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<UserProfile>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.DisplayName, deserialized.DisplayName);
        Assert.Equal(original.AvatarUrl, deserialized.AvatarUrl);
        Assert.Equal(original.Settings!["theme"], deserialized.Settings!["theme"]);
    }

    [Fact]
    public void SocialRelation_Roundtrip_PreservesAllData()
    {
        var original = new SocialRelation
        {
            TargetUserId = Guid.NewGuid(),
            RelationType = "Friend",
            Since = DateTimeOffset.UtcNow.AddDays(-100)
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<SocialRelation>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.TargetUserId, deserialized.TargetUserId);
        Assert.Equal(original.RelationType, deserialized.RelationType);
        Assert.Equal(original.Since, deserialized.Since);
    }

    #endregion

    #region Registry Models

    [Fact]
    public void ItemTypeDefinition_Roundtrip_PreservesAllData()
    {
        var original = new ItemTypeDefinition
        {
            ItemTypeId = "legendary_sword",
            Name = "Legendary Sword",
            Description = "A powerful weapon",
            Category = "Weapon",
            MaxStackSize = 1,
            IsTradeable = true
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<ItemTypeDefinition>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ItemTypeId, deserialized.ItemTypeId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.Category, deserialized.Category);
        Assert.Equal(original.MaxStackSize, deserialized.MaxStackSize);
        Assert.Equal(original.IsTradeable, deserialized.IsTradeable);
    }

    #endregion

    #region Null Handling

    [Fact]
    public void Item_WithNullMetadata_Roundtrip_Succeeds()
    {
        var original = new Item
        {
            Id = Guid.NewGuid(),
            ItemTypeId = "basic_sword",
            Quantity = 1,
            Metadata = null,
            AcquiredAt = DateTimeOffset.UtcNow
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<Item>(bytes);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Metadata);
    }

    [Fact]
    public void CharacterHistoryEntry_WithNullData_Roundtrip_Succeeds()
    {
        var original = new CharacterHistoryEntry
        {
            EventType = "Custom",
            Description = "Something happened",
            Timestamp = DateTimeOffset.UtcNow,
            Data = null
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<CharacterHistoryEntry>(bytes);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Data);
    }

    #endregion

    #region Empty Collections

    [Fact]
    public void Account_WithEmptyCollections_Roundtrip_Succeeds()
    {
        var original = new Account
        {
            AccountId = Guid.NewGuid(),
            UnlockedCosmetics = new List<string>(),
            UnlockedAchievements = new List<string>()
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<Account>(bytes);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.UnlockedCosmetics);
        Assert.Empty(deserialized.UnlockedAchievements);
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
            ItemTypeId = "test_item",
            Quantity = 10,
            AcquiredAt = DateTimeOffset.UtcNow
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
