using Orleans.TestingHost;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Unit tests for CharacterInventoryGrain.
/// </summary>
[Collection(ClusterCollection.Name)]
public class CharacterInventoryGrainTests
{
    private readonly TestCluster _cluster;

    public CharacterInventoryGrainTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
    }

    private async Task<string> SeedBaseType(int requiredLevel = 1, int requiredStr = 0, int requiredDex = 0, int requiredInt = 0)
    {
        var baseTypeId = $"inv_{Guid.NewGuid():N}";
        var registry = _cluster.GrainFactory.GetGrain<IBaseTypeRegistryGrain>("default");
        var baseType = new BaseType
        {
            BaseTypeId = baseTypeId,
            Name = $"Test {baseTypeId}",
            Slot = EquipmentSlot.MainHand,
            Width = 1,
            Height = 2,
            RequiredLevel = requiredLevel,
            RequiredStrength = requiredStr,
            RequiredDexterity = requiredDex,
            RequiredIntelligence = requiredInt,
            Tags = new HashSet<string> { "test", "weapon" }
        };
        await registry.RegisterAsync(baseType);
        return baseTypeId;
    }

    [Fact]
    public async Task SetStatsAsync_UpdatesStats()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");

        // Act
        var stats = await grain.SetStatsAsync(10, 50, 30, 20);

        // Assert
        Assert.Equal(10, stats.Level);
        Assert.Equal(50, stats.Strength);
        Assert.Equal(30, stats.Dexterity);
        Assert.Equal(20, stats.Intelligence);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsCurrentStats()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");
        await grain.SetStatsAsync(15, 60, 40, 30);

        // Act
        var stats = await grain.GetStatsAsync();

        // Assert
        Assert.Equal(15, stats.Level);
        Assert.Equal(60, stats.Strength);
    }

    [Fact]
    public async Task AddToBagAsync_PlacesItem()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");
        var baseTypeId = await SeedBaseType();
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var result = await grain.AddToBagAsync(item, 0, 0);

        // Assert
        Assert.True(result);
        var bagItems = await grain.GetBagItemsAsync();
        Assert.Single(bagItems);
    }

    [Fact]
    public async Task AddToBagAutoAsync_FindsSpace()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");
        var baseTypeId = await SeedBaseType();
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var position = await grain.AddToBagAutoAsync(item);

        // Assert
        Assert.NotNull(position);
    }

    [Fact]
    public async Task RemoveFromBagAsync_RemovesItem()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");
        var baseTypeId = await SeedBaseType();
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await grain.AddToBagAsync(item, 0, 0);

        // Act
        var removed = await grain.RemoveFromBagAsync(item.Id);

        // Assert
        Assert.NotNull(removed);
        Assert.Equal(item.Id, removed.Id);
        var bagItems = await grain.GetBagItemsAsync();
        Assert.Empty(bagItems);
    }

    [Fact]
    public async Task EquipAsync_EquipsItem()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");
        var baseTypeId = await SeedBaseType(requiredLevel: 1, requiredStr: 10);
        
        await grain.SetStatsAsync(10, 50, 30, 20); // Has enough stats
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await grain.AddToBagAsync(item, 0, 0);

        // Act
        var result = await grain.EquipAsync(item.Id, EquipmentSlot.MainHand);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(item.Id, result.EquippedItem?.Id);
    }

    [Fact]
    public async Task EquipAsync_InsufficientStats_Fails()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");
        var baseTypeId = await SeedBaseType(requiredLevel: 50, requiredStr: 100);
        
        await grain.SetStatsAsync(10, 20, 20, 20); // Not enough stats
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 50,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await grain.AddToBagAsync(item, 0, 0);

        // Act
        var result = await grain.EquipAsync(item.Id, EquipmentSlot.MainHand);

        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task UnequipAsync_ReturnsItemToBag()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");
        var baseTypeId = await SeedBaseType(requiredLevel: 1, requiredStr: 10);
        
        await grain.SetStatsAsync(10, 50, 30, 20);
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await grain.AddToBagAsync(item, 0, 0);
        await grain.EquipAsync(item.Id, EquipmentSlot.MainHand);

        // Act
        var unequipped = await grain.UnequipAsync(EquipmentSlot.MainHand);

        // Assert
        Assert.NotNull(unequipped);
        Assert.Equal(item.Id, unequipped.Id);
        
        var equipped = await grain.GetEquippedAsync();
        Assert.Empty(equipped);
        
        var bagItems = await grain.GetBagItemsAsync();
        Assert.Single(bagItems);
    }

    [Fact]
    public async Task MoveBagItemAsync_MovesItem()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");
        var baseTypeId = await SeedBaseType();
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await grain.AddToBagAsync(item, 0, 0);

        // Act
        var result = await grain.MoveBagItemAsync(item.Id, 5, 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasSpaceAsync_ReturnsCorrectly()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<ICharacterInventoryGrain>(characterId, "test_season");

        // Act & Assert - small item fits
        var hasSpaceSmall = await grain.HasSpaceAsync(2, 2);
        Assert.True(hasSpaceSmall);

        // Act & Assert - huge item doesn't fit
        var hasSpaceHuge = await grain.HasSpaceAsync(100, 100);
        Assert.False(hasSpaceHuge);
    }
}
