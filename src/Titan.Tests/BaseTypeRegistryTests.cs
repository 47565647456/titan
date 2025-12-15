using Orleans.TestingHost;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Unit tests for BaseTypeRegistryGrain.
/// Note: Reader grain tests are omitted due to caching behavior in stateless workers.
/// </summary>
[Collection(ClusterCollection.Name)]
public class BaseTypeRegistryTests
{
    private readonly TestCluster _cluster;

    public BaseTypeRegistryTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
    }

    [Fact]
    public async Task RegisterAsync_AddsBaseType()
    {
        // Arrange
        var baseTypeId = $"test_sword_{Guid.NewGuid():N}";
        var registry = _cluster.GrainFactory.GetGrain<IBaseTypeRegistryGrain>("default");
        var baseType = TestItemFactory.CreateTestBaseType(baseTypeId, 1, 3, EquipmentSlot.MainHand);

        // Act
        await registry.RegisterAsync(baseType);

        // Assert
        var retrieved = await registry.GetAsync(baseTypeId);
        Assert.NotNull(retrieved);
        Assert.Equal(baseTypeId, retrieved.BaseTypeId);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IBaseTypeRegistryGrain>("default");

        // Act
        var result = await registry.GetAsync($"nonexistent_{Guid.NewGuid():N}");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RegisterAsync_UpdatesExisting()
    {
        // Arrange
        var baseTypeId = $"update_test_{Guid.NewGuid():N}";
        var registry = _cluster.GrainFactory.GetGrain<IBaseTypeRegistryGrain>("default");
        var v1 = new BaseType
        {
            BaseTypeId = baseTypeId,
            Name = "Version 1",
            Slot = EquipmentSlot.MainHand,
            Width = 1,
            Height = 2
        };
        await registry.RegisterAsync(v1);
        
        var v2 = new BaseType
        {
            BaseTypeId = baseTypeId,
            Name = "Version 2",
            Slot = EquipmentSlot.MainHand,
            Width = 2,
            Height = 3
        };

        // Act
        await registry.RegisterAsync(v2);

        // Assert
        var retrieved = await registry.GetAsync(baseTypeId);
        Assert.NotNull(retrieved);
        Assert.Equal("Version 2", retrieved.Name);
        Assert.Equal(2, retrieved.Width);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsCorrectly()
    {
        // Arrange
        var baseTypeId = $"exists_test_{Guid.NewGuid():N}";
        var registry = _cluster.GrainFactory.GetGrain<IBaseTypeRegistryGrain>("default");
        var baseType = TestItemFactory.CreateTestBaseType(baseTypeId, 1, 1, EquipmentSlot.Helmet);

        // Act - before registration
        var beforeExists = await registry.ExistsAsync(baseTypeId);
        
        await registry.RegisterAsync(baseType);
        
        // Act - after registration
        var afterExists = await registry.ExistsAsync(baseTypeId);

        // Assert
        Assert.False(beforeExists);
        Assert.True(afterExists);
    }

    [Fact]
    public async Task GetByCategoryAsync_ReturnsMatchingTypes()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IBaseTypeRegistryGrain>("default");
        
        var equipId = $"equip_{Guid.NewGuid():N}";
        var currencyId = $"currency_{Guid.NewGuid():N}";
        
        var equipment = new BaseType
        {
            BaseTypeId = equipId,
            Name = "Equipment Item",
            Slot = EquipmentSlot.MainHand,
            Category = ItemCategory.Equipment
        };
        var currency = new BaseType
        {
            BaseTypeId = currencyId,
            Name = "Currency Item",
            Slot = EquipmentSlot.None,
            Category = ItemCategory.Currency
        };
        
        await registry.RegisterAsync(equipment);
        await registry.RegisterAsync(currency);

        // Act
        var equipItems = await registry.GetByCategoryAsync(ItemCategory.Equipment);
        var currencyItems = await registry.GetByCategoryAsync(ItemCategory.Currency);

        // Assert
        Assert.Contains(equipItems, e => e.BaseTypeId == equipId);
        Assert.Contains(currencyItems, c => c.BaseTypeId == currencyId);
    }
}
