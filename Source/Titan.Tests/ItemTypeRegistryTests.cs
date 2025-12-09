using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for the ItemTypeRegistry grain.
/// </summary>
public class ItemTypeRegistryTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task RegisterAsync_ShouldStoreItemType()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        var definition = new ItemTypeDefinition
        {
            ItemTypeId = "test_sword",
            Name = "Test Sword",
            MaxStackSize = 1,
            IsTradeable = true
        };

        // Act
        await registry.RegisterAsync(definition);
        var result = await registry.GetAsync("test_sword");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test_sword", result.ItemTypeId);
        Assert.Equal("Test Sword", result.Name);
        Assert.Equal(1, result.MaxStackSize);
        Assert.True(result.IsTradeable);
    }

    [Fact]
    public async Task RegisterManyAsync_ShouldStoreMultipleItemTypes()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        var definitions = new[]
        {
            new ItemTypeDefinition { ItemTypeId = "batch_item_1", Name = "Batch Item 1" },
            new ItemTypeDefinition { ItemTypeId = "batch_item_2", Name = "Batch Item 2" },
            new ItemTypeDefinition { ItemTypeId = "batch_item_3", Name = "Batch Item 3" }
        };

        // Act
        await registry.RegisterManyAsync(definitions);
        var all = await registry.GetAllAsync();

        // Assert
        Assert.True(all.Count >= 3);
        Assert.Contains(all, d => d.ItemTypeId == "batch_item_1");
        Assert.Contains(all, d => d.ItemTypeId == "batch_item_2");
        Assert.Contains(all, d => d.ItemTypeId == "batch_item_3");
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrueForRegisteredType()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        await registry.RegisterAsync(new ItemTypeDefinition { ItemTypeId = "exists_test", Name = "Exists Test" });

        // Act & Assert
        Assert.True(await registry.ExistsAsync("exists_test"));
        Assert.False(await registry.ExistsAsync("nonexistent_item"));
    }

    [Fact]
    public async Task GetMaxStackSizeAsync_ShouldReturnConfiguredValue()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        await registry.RegisterAsync(new ItemTypeDefinition
        {
            ItemTypeId = "stackable_potion",
            Name = "Stackable Potion",
            MaxStackSize = 99
        });

        // Act
        var stackSize = await registry.GetMaxStackSizeAsync("stackable_potion");
        var unknownStackSize = await registry.GetMaxStackSizeAsync("unknown_item");

        // Assert
        Assert.Equal(99, stackSize);
        Assert.Equal(1, unknownStackSize); // Default for unknown
    }

    [Fact]
    public async Task IsTradeableAsync_ShouldReturnConfiguredValue()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        await registry.RegisterAsync(new ItemTypeDefinition
        {
            ItemTypeId = "soulbound_item",
            Name = "Soulbound Item",
            IsTradeable = false
        });

        // Act
        var soulboundTradeable = await registry.IsTradeableAsync("soulbound_item");
        var unknownTradeable = await registry.IsTradeableAsync("unknown_item");

        // Assert
        Assert.False(soulboundTradeable);
        Assert.True(unknownTradeable); // Default for unknown
    }

    [Fact]
    public async Task UpdateAsync_ShouldModifyExistingItemType()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        await registry.RegisterAsync(new ItemTypeDefinition
        {
            ItemTypeId = "update_test",
            Name = "Original Name",
            MaxStackSize = 5
        });

        // Act
        await registry.UpdateAsync(new ItemTypeDefinition
        {
            ItemTypeId = "update_test",
            Name = "Updated Name",
            MaxStackSize = 10
        });
        var result = await registry.GetAsync("update_test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Updated Name", result.Name);
        Assert.Equal(10, result.MaxStackSize);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveItemType()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        await registry.RegisterAsync(new ItemTypeDefinition { ItemTypeId = "delete_test", Name = "Delete Test" });
        Assert.True(await registry.ExistsAsync("delete_test"));

        // Act
        await registry.DeleteAsync("delete_test");

        // Assert
        Assert.False(await registry.ExistsAsync("delete_test"));
    }

    [Fact]
    public async Task RegisterAsync_WithInvalidStackSize_ShouldThrow()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IItemTypeRegistryGrain>("default");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            registry.RegisterAsync(new ItemTypeDefinition
            {
                ItemTypeId = "invalid_stack",
                Name = "Invalid Stack",
                MaxStackSize = 0
            }));
    }
}
