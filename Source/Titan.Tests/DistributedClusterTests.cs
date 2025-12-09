using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for verifying distributed behavior across multiple Orleans silos.
/// </summary>
[Trait("Category", "Distributed")]
public class DistributedClusterTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private const string TestSeasonId = "standard";

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder(2);
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
    }

    private async Task<Guid> CreateTestCharacterAsync()
    {
        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, TestSeasonId);
        await charGrain.InitializeAsync(accountId, $"TestChar_{charId:N}", CharacterRestrictions.None);
        return charId;
    }

    [Fact]
    public void Cluster_ShouldHaveMultipleSilos()
    {
        Assert.Equal(2, _cluster.Silos.Count);
    }

    [Fact]
    public async Task GrainState_ShouldSurviveSiloFailure()
    {
        var charId = await CreateTestCharacterAsync();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
        var item = await inventory.AddItemAsync("SurvivalSword", 1);

        var initialSiloCount = _cluster.Silos.Count;
        var secondarySilo = _cluster.Silos[1];
        await _cluster.StopSiloAsync(secondarySilo);

        Assert.Equal(initialSiloCount - 1, _cluster.Silos.Count);
        var items = await inventory.GetItemsAsync();
        Assert.NotNull(items);
    }

    [Fact]
    public async Task CrossSiloTrade_ShouldCompleteSuccessfully()
    {
        var charA = await CreateTestCharacterAsync();
        var charB = await CreateTestCharacterAsync();
        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charA, TestSeasonId);
        var inventoryB = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charB, TestSeasonId);

        var itemA = await inventoryA.AddItemAsync("CrossSiloSword", 1);
        var itemB = await inventoryB.AddItemAsync("CrossSiloShield", 1);

        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);
        
        await tradeGrain.AddItemAsync(charA, itemA.Id);
        await tradeGrain.AddItemAsync(charB, itemB.Id);
        
        await tradeGrain.AcceptAsync(charA);
        var finalStatus = await tradeGrain.AcceptAsync(charB);

        Assert.Equal(TradeStatus.Completed, finalStatus);
        
        Assert.False(await inventoryA.HasItemAsync(itemA.Id));
        Assert.False(await inventoryB.HasItemAsync(itemB.Id));
        Assert.True(await inventoryA.HasItemAsync(itemB.Id));
        Assert.True(await inventoryB.HasItemAsync(itemA.Id));
    }

    [Fact]
    public async Task HighConcurrency_ManyGrains_ShouldDistribute()
    {
        var tasks = Enumerable.Range(0, 50).Select(async i =>
        {
            var charId = await CreateTestCharacterAsync();
            var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
            await inventory.AddItemAsync($"ConcurrentItem{i}", 1);
            return charId;
        });

        var charIds = await Task.WhenAll(tasks);

        foreach (var charId in charIds)
        {
            var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
            var items = await inventory.GetItemsAsync();
            Assert.Single(items);
        }

        Assert.Equal(50, charIds.Length);
    }

    [Fact]
    public async Task GracefulDegradation_WhenSiloGoesDown()
    {
        var chars = new List<(Guid CharId, Guid ItemId)>();
        for (int i = 0; i < 10; i++)
        {
            var charId = await CreateTestCharacterAsync();
            var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
            var item = await inventory.AddItemAsync($"DegradationItem{i}", i + 1);
            chars.Add((charId, item.Id));
        }

        var siloToStop = _cluster.Silos[1];
        await _cluster.StopSiloAsync(siloToStop);

        foreach (var (charId, _) in chars)
        {
            var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
            var items = await inventory.GetItemsAsync();
            Assert.NotNull(items);
        }
    }

    [Fact]
    public async Task SocialGraph_CrossSilo_ShouldWork()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var userC = Guid.NewGuid();

        var socialA = _cluster.GrainFactory.GetGrain<ISocialGrain>(userA);
        var socialB = _cluster.GrainFactory.GetGrain<ISocialGrain>(userB);

        await socialA.AddRelationAsync(userB, "Friend");
        await socialA.AddRelationAsync(userC, "Friend");
        await socialB.AddRelationAsync(userA, "Friend");

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
        var trades = new List<Task<TradeStatus>>();
        
        for (int i = 0; i < 5; i++)
        {
            var tradeIndex = i;
            var trade = Task.Run(async () =>
            {
                var charA = await CreateTestCharacterAsync();
                var charB = await CreateTestCharacterAsync();

                var invA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charA, TestSeasonId);
                var invB = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charB, TestSeasonId);

                var itemA = await invA.AddItemAsync($"ParallelItemA_{tradeIndex}", 1);
                var itemB = await invB.AddItemAsync($"ParallelItemB_{tradeIndex}", 1);

                var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(Guid.NewGuid());
                await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);
                await tradeGrain.AddItemAsync(charA, itemA.Id);
                await tradeGrain.AddItemAsync(charB, itemB.Id);
                await tradeGrain.AcceptAsync(charA);
                return await tradeGrain.AcceptAsync(charB);
            });

            trades.Add(trade);
        }

        var results = await Task.WhenAll(trades);
        Assert.All(results, status => Assert.Equal(TradeStatus.Completed, status));
    }

    #region Database-Specific Distributed Tests

    [Fact]
    [Trait("Category", "Database")]
    public async Task Database_SiloFailure_ShouldRetainInventoryState()
    {
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        var charId = await CreateTestCharacterAsync();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
        var item = await inventory.AddItemAsync("DatabasePersistentSword", 1);
        var itemId = item.Id;

        var siloToKill = _cluster.Silos[1];
        await _cluster.StopSiloAsync(siloToKill);

        var inventoryAfter = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
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
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        var charA = await CreateTestCharacterAsync();
        var charB = await CreateTestCharacterAsync();

        var invA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charA, TestSeasonId);
        var invB = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charB, TestSeasonId);

        var itemA = await invA.AddItemAsync("MidTradeItemA", 1);
        var itemB = await invB.AddItemAsync("MidTradeItemB", 1);

        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);
        await tradeGrain.AddItemAsync(charA, itemA.Id);
        await tradeGrain.AddItemAsync(charB, itemB.Id);
        
        await tradeGrain.AcceptAsync(charA);

        await _cluster.StopSiloAsync(_cluster.Silos[1]);

        var tradeGrainAfter = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        var session = await tradeGrainAfter.GetSessionAsync();
        
        Assert.Equal(TradeStatus.Pending, session.Status);
        Assert.True(session.InitiatorAccepted, "Initiator acceptance should persist");
        Assert.False(session.TargetAccepted, "Target should not be accepted yet");
        Assert.Contains(itemA.Id, session.InitiatorItemIds);

        var finalStatus = await tradeGrainAfter.AcceptAsync(charB);
        Assert.Equal(TradeStatus.Completed, finalStatus);

        var invAAfter = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charA, TestSeasonId);
        var invBAfter = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charB, TestSeasonId);
        Assert.True(await invAAfter.HasItemAsync(itemB.Id), "User A should have Item B");
        Assert.True(await invBAfter.HasItemAsync(itemA.Id), "User B should have Item A");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Database_AllSilosRestart_ShouldRetainState()
    {
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        var charId = await CreateTestCharacterAsync();
        var userId = Guid.NewGuid();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
        var identity = _cluster.GrainFactory.GetGrain<IUserIdentityGrain>(userId);
        var social = _cluster.GrainFactory.GetGrain<ISocialGrain>(userId);

        var item = await inventory.AddItemAsync("ClusterRestartItem", 5);
        await identity.LinkProviderAsync("Steam", "steam_cluster_test");
        var friendId = Guid.NewGuid();
        await social.AddRelationAsync(friendId, "Friend");

        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        var inventoryAfter = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
        var identityAfter = _cluster.GrainFactory.GetGrain<IUserIdentityGrain>(userId);
        var socialAfter = _cluster.GrainFactory.GetGrain<ISocialGrain>(userId);

        Assert.True(await inventoryAfter.HasItemAsync(item.Id), "Inventory should persist across cluster restart");
        var fetchedItem = await inventoryAfter.GetItemAsync(item.Id);
        Assert.Equal(5, fetchedItem!.Quantity);

        var identityData = await identityAfter.GetIdentityAsync();
        Assert.Contains(identityData.LinkedProviders, p => p.ProviderName == "Steam");

        var relations = await socialAfter.GetRelationsAsync();
        Assert.Single(relations);
        Assert.Equal(friendId, relations[0].TargetUserId);
    }

    #endregion
}
