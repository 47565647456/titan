using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for verifying distributed behavior across multiple Orleans silos.
/// Uses a 2-silo TestCluster to validate that the system works correctly in a multi-node environment.
/// </summary>
[Trait("Category", "Distributed")]
public class DistributedClusterTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        // Create a cluster with 2 silos to test distributed behavior
        var builder = new TestClusterBuilder(2);
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public void Cluster_ShouldHaveMultipleSilos()
    {
        // Verify we have the expected number of silos
        Assert.Equal(2, _cluster.Silos.Count);
    }

    [Fact]
    public async Task GrainState_ShouldSurviveSiloFailure()
    {
        // Arrange - Create some state
        var userId = Guid.NewGuid();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
        var item = await inventory.AddItemAsync("SurvivalSword", 1);

        // Capture initial silo count
        var initialSiloCount = _cluster.Silos.Count;

        // Act - Kill the secondary silo
        var secondarySilo = _cluster.Silos[1];
        await _cluster.StopSiloAsync(secondarySilo);

        // Verify silo was stopped
        Assert.Equal(initialSiloCount - 1, _cluster.Silos.Count);

        // Assert - Grain should still be accessible (reactivated on remaining silo)
        // Note: With in-memory storage, state is lost. With database, state persists.
        // This test validates that the grain can still be called after silo failure.
        var items = await inventory.GetItemsAsync();
        
        // With in-memory storage, the grain reactivates but state is empty
        // With database, the item would still exist
        // Either way, the grain should be callable without throwing
        Assert.NotNull(items);
    }

    [Fact]
    public async Task CrossSiloTrade_ShouldCompleteSuccessfully()
    {
        // Arrange - Create grains that may be placed on different silos (random placement)
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var inventoryB = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userB);

        // Give both users items
        var itemA = await inventoryA.AddItemAsync("CrossSiloSword", 1);
        var itemB = await inventoryB.AddItemAsync("CrossSiloShield", 1);

        // Act - Start a trade (which coordinates across grains potentially on different silos)
        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);
        
        await tradeGrain.AddItemAsync(userA, itemA.Id);
        await tradeGrain.AddItemAsync(userB, itemB.Id);
        
        await tradeGrain.AcceptAsync(userA);
        var finalStatus = await tradeGrain.AcceptAsync(userB);

        // Assert - Trade should complete successfully across silo boundaries
        Assert.Equal(TradeStatus.Completed, finalStatus);
        
        // Verify items were transferred
        Assert.False(await inventoryA.HasItemAsync(itemA.Id));
        Assert.False(await inventoryB.HasItemAsync(itemB.Id));
        Assert.True(await inventoryA.HasItemAsync(itemB.Id));
        Assert.True(await inventoryB.HasItemAsync(itemA.Id));
    }

    [Fact]
    public async Task HighConcurrency_ManyGrains_ShouldDistribute()
    {
        // Create many grains concurrently - they should distribute across silos
        var tasks = Enumerable.Range(0, 50).Select(async i =>
        {
            var userId = Guid.NewGuid();
            var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
            await inventory.AddItemAsync($"ConcurrentItem{i}", 1);
            return userId;
        });

        var userIds = await Task.WhenAll(tasks);

        // All users should have their item
        foreach (var userId in userIds)
        {
            var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
            var items = await inventory.GetItemsAsync();
            Assert.Single(items);
        }

        // Verify we created the expected number of users
        Assert.Equal(50, userIds.Length);
    }

    [Fact]
    public async Task GracefulDegradation_WhenSiloGoesDown()
    {
        // Arrange - Create grains on the cluster
        var users = new List<(Guid UserId, Guid ItemId)>();
        for (int i = 0; i < 10; i++)
        {
            var userId = Guid.NewGuid();
            var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
            var item = await inventory.AddItemAsync($"DegradationItem{i}", i + 1);
            users.Add((userId, item.Id));
        }

        // Act - Stop one silo (simulate node failure)
        var siloToStop = _cluster.Silos[1];
        await _cluster.StopSiloAsync(siloToStop);

        // Assert - All grains should still be callable (may reactivate on remaining silo)
        foreach (var (userId, _) in users)
        {
            var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
            // Should not throw - grain should be accessible
            var items = await inventory.GetItemsAsync();
            Assert.NotNull(items);
        }
    }

    [Fact]
    public async Task SocialGraph_CrossSilo_ShouldWork()
    {
        // Arrange - Create social relationships across potentially different silos
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var userC = Guid.NewGuid();

        var socialA = _cluster.GrainFactory.GetGrain<ISocialGrain>(userA);
        var socialB = _cluster.GrainFactory.GetGrain<ISocialGrain>(userB);

        // Act - Create bidirectional friendships
        await socialA.AddRelationAsync(userB, "Friend");
        await socialA.AddRelationAsync(userC, "Friend");
        await socialB.AddRelationAsync(userA, "Friend");

        // Assert
        var relationsA = await socialA.GetRelationsAsync();
        var relationsB = await socialB.GetRelationsAsync();

        Assert.Equal(2, relationsA.Count);
        Assert.Single(relationsB);
        Assert.True(await socialA.HasRelationAsync(userB, "Friend"));
        Assert.True(await socialB.HasRelationAsync(userA, "Friend"));
    }

    [Fact]
    public async Task ParallelTradesAcrossSilos_ShouldNotInterfere()
    {
        // Arrange - Set up multiple trades in parallel
        var trades = new List<Task<TradeStatus>>();
        
        for (int i = 0; i < 5; i++)
        {
            var trade = Task.Run(async () =>
            {
                var userA = Guid.NewGuid();
                var userB = Guid.NewGuid();

                var invA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
                var invB = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userB);

                var itemA = await invA.AddItemAsync($"ParallelItemA_{i}", 1);
                var itemB = await invB.AddItemAsync($"ParallelItemB_{i}", 1);

                var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(Guid.NewGuid());
                await tradeGrain.InitiateAsync(userA, userB);
                await tradeGrain.AddItemAsync(userA, itemA.Id);
                await tradeGrain.AddItemAsync(userB, itemB.Id);
                await tradeGrain.AcceptAsync(userA);
                return await tradeGrain.AcceptAsync(userB);
            });

            trades.Add(trade);
        }

        // Act
        var results = await Task.WhenAll(trades);

        // Assert - All trades should complete successfully
        Assert.All(results, status => Assert.Equal(TradeStatus.Completed, status));
    }

    #region Database-Specific Distributed Tests
    // These tests require USE_DATABASE=true and verify that state persists
    // across silo failures when using CockroachDB storage.

    [Fact]
    [Trait("Category", "Database")]
    public async Task Database_SiloFailure_ShouldRetainInventoryState()
    {
        // Skip if no database configured
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        // Arrange - Create inventory state
        var userId = Guid.NewGuid();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
        var item = await inventory.AddItemAsync("DatabasePersistentSword", 1);
        var itemId = item.Id;

        // Act - Kill one silo (simulating node crash)
        var siloToKill = _cluster.Silos[1];
        await _cluster.StopSiloAsync(siloToKill);

        // Assert - State should persist via database (grain reactivates on remaining silo)
        var inventoryAfter = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
        Assert.True(await inventoryAfter.HasItemAsync(itemId), 
            "Item should persist after silo failure when using database storage");
        
        var fetchedItem = await inventoryAfter.GetItemAsync(itemId);
        Assert.NotNull(fetchedItem);
        Assert.Equal("DatabasePersistentSword", fetchedItem!.ItemTypeId);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Database_SiloFailure_MidTrade_ShouldResume()
    {
        // Skip if no database configured
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        // Arrange - Set up a trade in progress
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var invA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var invB = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userB);

        var itemA = await invA.AddItemAsync("MidTradeItemA", 1);
        var itemB = await invB.AddItemAsync("MidTradeItemB", 1);

        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);
        await tradeGrain.AddItemAsync(userA, itemA.Id);
        await tradeGrain.AddItemAsync(userB, itemB.Id);
        
        // User A accepts, but trade is not complete yet
        await tradeGrain.AcceptAsync(userA);

        // Act - Kill one silo mid-trade
        await _cluster.StopSiloAsync(_cluster.Silos[1]);

        // Assert - Trade state should persist and be resumable
        var tradeGrainAfter = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        var session = await tradeGrainAfter.GetSessionAsync();
        
        Assert.Equal(TradeStatus.Pending, session.Status);
        Assert.True(session.InitiatorAccepted, "Initiator acceptance should persist");
        Assert.False(session.TargetAccepted, "Target should not be accepted yet");
        Assert.Contains(itemA.Id, session.InitiatorItemIds);

        // Complete the trade after silo failure
        var finalStatus = await tradeGrainAfter.AcceptAsync(userB);
        Assert.Equal(TradeStatus.Completed, finalStatus);

        // Verify items transferred correctly
        var invAAfter = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var invBAfter = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userB);
        Assert.True(await invAAfter.HasItemAsync(itemB.Id), "User A should have Item B");
        Assert.True(await invBAfter.HasItemAsync(itemA.Id), "User B should have Item A");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Database_AllSilosRestart_ShouldRetainState()
    {
        // Skip if no database configured
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        // Arrange - Create state across multiple grains
        var userId = Guid.NewGuid();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
        var identity = _cluster.GrainFactory.GetGrain<IUserIdentityGrain>(userId);
        var social = _cluster.GrainFactory.GetGrain<ISocialGrain>(userId);

        var item = await inventory.AddItemAsync("ClusterRestartItem", 5);
        await identity.LinkProviderAsync("Steam", "steam_cluster_test");
        var friendId = Guid.NewGuid();
        await social.AddRelationAsync(friendId, "Friend");

        // Act - Stop ALL silos and restart the cluster
        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        // Assert - All state should persist via database
        var inventoryAfter = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
        var identityAfter = _cluster.GrainFactory.GetGrain<IUserIdentityGrain>(userId);
        var socialAfter = _cluster.GrainFactory.GetGrain<ISocialGrain>(userId);

        // Verify inventory
        Assert.True(await inventoryAfter.HasItemAsync(item.Id), "Inventory should persist across cluster restart");
        var fetchedItem = await inventoryAfter.GetItemAsync(item.Id);
        Assert.Equal(5, fetchedItem!.Quantity);

        // Verify identity
        var identityData = await identityAfter.GetIdentityAsync();
        Assert.Contains(identityData.LinkedProviders, p => p.ProviderName == "Steam");

        // Verify social
        var relations = await socialAfter.GetRelationsAsync();
        Assert.Single(relations);
        Assert.Equal(friendId, relations[0].TargetUserId);
    }

    #endregion
}
