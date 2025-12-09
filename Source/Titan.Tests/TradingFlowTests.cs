using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Integration tests for trading flow using Orleans TestCluster with CockroachDB persistence.
/// Each test class creates its own cluster with real database integration.
/// </summary>
public class TradingFlowTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task Trade_BetweenTwoPlayers_ShouldTransferItems()
    {
        // Arrange
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var inventoryB = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userB);

        // Give each player some items
        var itemA = await inventoryA.AddItemAsync("Sword", 1);
        var itemB = await inventoryB.AddItemAsync("Shield", 1);

        // Act - Start a trade
        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Both players add their items
        await tradeGrain.AddItemAsync(userA, itemA.Id);
        await tradeGrain.AddItemAsync(userB, itemB.Id);

        // Both accept
        await tradeGrain.AcceptAsync(userA);
        var finalStatus = await tradeGrain.AcceptAsync(userB);

        // Assert
        Assert.Equal(TradeStatus.Completed, finalStatus);

        // Verify items were removed from original owners
        Assert.False(await inventoryA.HasItemAsync(itemA.Id));
        Assert.False(await inventoryB.HasItemAsync(itemB.Id));
    }

    [Fact]
    public async Task Trade_Gift_ShouldAllowOneSidedTransfer()
    {
        // Arrange - Gifting scenario (user A gives, user B receives nothing in return)
        var giver = Guid.NewGuid();
        var receiver = Guid.NewGuid();

        var giverInventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(giver);
        var gift = await giverInventory.AddItemAsync("GiftItem", 1);

        // Act
        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(giver, receiver);

        // Only giver adds an item
        await tradeGrain.AddItemAsync(giver, gift.Id);

        // Both accept
        await tradeGrain.AcceptAsync(giver);
        var finalStatus = await tradeGrain.AcceptAsync(receiver);

        // Assert
        Assert.Equal(TradeStatus.Completed, finalStatus);
        Assert.False(await giverInventory.HasItemAsync(gift.Id));
    }

    [Fact]
    public async Task Inventory_AddAndRemoveItems_ShouldWork()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);

        // Act
        var item = await inventory.AddItemAsync("TestItem", 5);
        var fetched = await inventory.GetItemAsync(item.Id);

        // Assert
        Assert.NotNull(fetched);
        Assert.Equal("TestItem", fetched!.ItemTypeId);
        Assert.Equal(5, fetched.Quantity);

        // Remove
        var removed = await inventory.RemoveItemAsync(item.Id);
        Assert.True(removed);
        Assert.False(await inventory.HasItemAsync(item.Id));
    }

    [Fact]
    public async Task UserIdentity_LinkProvider_ShouldPersist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var identityGrain = _cluster.GrainFactory.GetGrain<IUserIdentityGrain>(userId);

        // Act
        await identityGrain.LinkProviderAsync("Steam", "steam_12345");
        await identityGrain.LinkProviderAsync("EOS", "eos_67890");
        var identity = await identityGrain.GetIdentityAsync();

        // Assert
        Assert.Equal(2, identity.LinkedProviders.Count);
        Assert.True(await identityGrain.HasProviderAsync("Steam"));
        Assert.True(await identityGrain.HasProviderAsync("EOS"));
    }
}

public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        // Use environment variable to determine storage type
        // CI sets USE_DATABASE=true, local dev defaults to memory
        var useDatabase = Environment.GetEnvironmentVariable("USE_DATABASE") == "true";
        
        if (useDatabase)
        {
            // Use real CockroachDB for integration tests
            siloBuilder.AddAdoNetGrainStorage("OrleansStorage", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = Environment.GetEnvironmentVariable("COCKROACH_CONNECTION") 
                    ?? "Host=localhost;Port=26257;Database=titan;Username=root;SSL Mode=Disable";
            });
        }
        else
        {
            // Use in-memory storage for fast local development
            siloBuilder.AddMemoryGrainStorage("OrleansStorage");
        }
    }
}
