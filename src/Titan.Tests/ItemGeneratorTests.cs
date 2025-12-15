using Orleans.TestingHost;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Unit tests for ItemGeneratorGrain.
/// </summary>
[Collection(ClusterCollection.Name)]
public class ItemGeneratorTests
{
    private readonly TestCluster _cluster;

    public ItemGeneratorTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
    }

    private async Task<string> SeedBaseType(int width = 1, int height = 1, EquipmentSlot slot = EquipmentSlot.None, int maxSockets = 0)
    {
        var baseTypeId = $"gen_{Guid.NewGuid():N}";
        var registry = _cluster.GrainFactory.GetGrain<IBaseTypeRegistryGrain>("default");
        var baseType = new BaseType
        {
            BaseTypeId = baseTypeId,
            Name = $"Test {baseTypeId}",
            Slot = slot,
            Width = width,
            Height = height,
            MaxSockets = maxSockets,
            RequiredStrength = 10,
            RequiredDexterity = 10,
            RequiredIntelligence = 10,
            Tags = new HashSet<string> { "test", "weapon" }
        };
        await registry.RegisterAsync(baseType);
        return baseTypeId;
    }

    [Fact]
    public async Task GenerateAsync_Normal_CreatesItem()
    {
        // Arrange
        var baseTypeId = await SeedBaseType(1, 2, EquipmentSlot.MainHand);
        var generator = _cluster.GrainFactory.GetGrain<IItemGeneratorGrain>("default");

        // Act
        var item = await generator.GenerateAsync(baseTypeId, 10);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(baseTypeId, item.BaseTypeId);
        Assert.Equal(10, item.ItemLevel);
        Assert.Equal(ItemRarity.Normal, item.Rarity);
    }

    [Fact]
    public async Task GenerateAsync_Magic_ReturnsItem()
    {
        // Arrange
        var baseTypeId = await SeedBaseType(1, 2, EquipmentSlot.MainHand);
        var generator = _cluster.GrainFactory.GetGrain<IItemGeneratorGrain>("default");

        // Act
        var item = await generator.GenerateAsync(baseTypeId, 10, ItemRarity.Magic);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(ItemRarity.Magic, item.Rarity);
    }

    [Fact]
    public async Task GenerateAsync_Rare_ReturnsItem()
    {
        // Arrange
        var baseTypeId = await SeedBaseType(1, 2, EquipmentSlot.MainHand);
        var generator = _cluster.GrainFactory.GetGrain<IItemGeneratorGrain>("default");

        // Act
        var item = await generator.GenerateAsync(baseTypeId, 20, ItemRarity.Rare);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(ItemRarity.Rare, item.Rarity);
        Assert.False(string.IsNullOrEmpty(item.Name)); // Rare items get names
    }

    [Fact]
    public async Task GenerateAsync_WithSockets_HasSockets()
    {
        // Arrange
        var baseTypeId = await SeedBaseType(2, 3, EquipmentSlot.BodyArmour, maxSockets: 6);
        var generator = _cluster.GrainFactory.GetGrain<IItemGeneratorGrain>("default");

        // Act
        var item = await generator.GenerateAsync(baseTypeId, 50);

        // Assert
        Assert.NotNull(item);
        Assert.NotEmpty(item.Sockets);
    }

    [Fact]
    public async Task RollSocketsAsync_RespectsMaxSockets()
    {
        // Arrange
        var baseTypeId = await SeedBaseType(2, 3, EquipmentSlot.BodyArmour, maxSockets: 4);
        var generator = _cluster.GrainFactory.GetGrain<IItemGeneratorGrain>("default");

        // Act
        var sockets = await generator.RollSocketsAsync(baseTypeId, 50);

        // Assert
        Assert.True(sockets.Count <= 4);
    }

    [Fact]
    public async Task TransmuteAsync_NormalToMagic()
    {
        // Arrange
        var baseTypeId = await SeedBaseType(1, 2, EquipmentSlot.MainHand);
        var generator = _cluster.GrainFactory.GetGrain<IItemGeneratorGrain>("default");
        
        var normalItem = await generator.GenerateAsync(baseTypeId, 10);
        Assert.Equal(ItemRarity.Normal, normalItem.Rarity);

        // Act
        var magicItem = await generator.TransmuteAsync(normalItem);

        // Assert
        Assert.NotNull(magicItem);
        Assert.Equal(ItemRarity.Magic, magicItem.Rarity);
    }

    [Fact]
    public async Task TransmuteAsync_MagicItem_ReturnsNull()
    {
        // Arrange
        var baseTypeId = await SeedBaseType(1, 2, EquipmentSlot.MainHand);
        var generator = _cluster.GrainFactory.GetGrain<IItemGeneratorGrain>("default");
        
        var magicItem = await generator.GenerateAsync(baseTypeId, 10, ItemRarity.Magic);

        // Act
        var result = await generator.TransmuteAsync(magicItem);

        // Assert
        Assert.Null(result); // Can only transmute normal items
    }

    [Fact]
    public async Task JewellerAsync_RerollsSockets()
    {
        // Arrange
        var baseTypeId = await SeedBaseType(2, 3, EquipmentSlot.BodyArmour, maxSockets: 6);
        var generator = _cluster.GrainFactory.GetGrain<IItemGeneratorGrain>("default");
        
        var item = await generator.GenerateAsync(baseTypeId, 50);

        // Act
        var rerolled = await generator.JewellerAsync(item);

        // Assert
        Assert.NotNull(rerolled);
        Assert.NotEmpty(rerolled.Sockets);
    }
}
