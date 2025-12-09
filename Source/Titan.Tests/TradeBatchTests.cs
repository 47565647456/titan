using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for batch trade operations and trade limits.
/// </summary>
public class TradeBatchTests : IAsyncLifetime
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
    public async Task AddItemsAsync_ShouldAddMultipleItems()
    {
        // Arrange
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var item1 = await inventoryA.AddItemAsync("batch_sword_1", 1);
        var item2 = await inventoryA.AddItemAsync("batch_sword_2", 1);
        var item3 = await inventoryA.AddItemAsync("batch_sword_3", 1);

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Act - Add multiple items in one call
        await tradeGrain.AddItemsAsync(userA, new[] { item1.Id, item2.Id, item3.Id });

        // Assert
        var session = await tradeGrain.GetSessionAsync();
        Assert.Equal(3, session.InitiatorItemIds.Count);
        Assert.Contains(item1.Id, session.InitiatorItemIds);
        Assert.Contains(item2.Id, session.InitiatorItemIds);
        Assert.Contains(item3.Id, session.InitiatorItemIds);
    }

    [Fact]
    public async Task RemoveItemsAsync_ShouldRemoveMultipleItems()
    {
        // Arrange
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var item1 = await inventoryA.AddItemAsync("remove_batch_1", 1);
        var item2 = await inventoryA.AddItemAsync("remove_batch_2", 1);
        var item3 = await inventoryA.AddItemAsync("remove_batch_3", 1);

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);
        await tradeGrain.AddItemsAsync(userA, new[] { item1.Id, item2.Id, item3.Id });

        // Act - Remove two items
        await tradeGrain.RemoveItemsAsync(userA, new[] { item1.Id, item3.Id });

        // Assert
        var session = await tradeGrain.GetSessionAsync();
        Assert.Single(session.InitiatorItemIds);
        Assert.Contains(item2.Id, session.InitiatorItemIds);
    }

    [Fact]
    public async Task AddItemAsync_NonTradeableItem_ShouldThrow()
    {
        // Arrange - Register a non-tradeable item type
        var registry = _cluster.GrainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        await registry.RegisterAsync(new ItemTypeDefinition
        {
            ItemTypeId = "soulbound_gem",
            Name = "Soulbound Gem",
            IsTradeable = false
        });

        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var soulboundItem = await inventoryA.AddItemAsync("soulbound_gem", 1);

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tradeGrain.AddItemAsync(userA, soulboundItem.Id));
        Assert.Contains("not tradeable", ex.Message);
    }

    [Fact]
    public async Task AddItemsAsync_ExceedingLimit_ShouldThrow()
    {
        // Arrange - Test MaxItemsPerUser limit (set to 50 in config, we'll test with a smaller scenario)
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        
        // Create many items (we'll add them in batches to test the limit)
        var items = new List<Guid>();
        for (int i = 0; i < 60; i++)
        {
            var item = await inventoryA.AddItemAsync($"limit_test_item_{i}", 1);
            items.Add(item.Id);
        }

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Act & Assert - Adding 60 items should exceed the 50 item limit
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tradeGrain.AddItemsAsync(userA, items));
        Assert.Contains("exceed limit", ex.Message);
    }

    [Fact]
    public async Task BatchTrade_ShouldCompleteSuccessfully()
    {
        // Arrange
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var inventoryB = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userB);

        var itemA1 = await inventoryA.AddItemAsync("trade_item_a1", 1);
        var itemA2 = await inventoryA.AddItemAsync("trade_item_a2", 1);
        var itemB1 = await inventoryB.AddItemAsync("trade_item_b1", 1);

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Act - Both users add items in batch
        await tradeGrain.AddItemsAsync(userA, new[] { itemA1.Id, itemA2.Id });
        await tradeGrain.AddItemAsync(userB, itemB1.Id);

        await tradeGrain.AcceptAsync(userA);
        var status = await tradeGrain.AcceptAsync(userB);

        // Assert
        Assert.Equal(TradeStatus.Completed, status);

        // Verify items transferred
        Assert.False(await inventoryA.HasItemAsync(itemA1.Id));
        Assert.False(await inventoryA.HasItemAsync(itemA2.Id));
        Assert.True(await inventoryA.HasItemAsync(itemB1.Id));

        Assert.True(await inventoryB.HasItemAsync(itemA1.Id));
        Assert.True(await inventoryB.HasItemAsync(itemA2.Id));
        Assert.False(await inventoryB.HasItemAsync(itemB1.Id));
    }
}
