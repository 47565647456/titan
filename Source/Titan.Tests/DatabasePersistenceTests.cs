using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Xunit;
using System.Linq;

namespace Titan.Tests;

/// <summary>
/// Tests specifically focused on database persistence and concurrency guarantees.
/// Requires USE_DATABASE=true environment variable.
/// </summary>
public class DatabasePersistenceTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private const string TestSeasonId = "standard";

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

    private async Task<Guid> CreateTestCharacterAsync()
    {
        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, TestSeasonId);
        await charGrain.InitializeAsync(accountId, $"TestChar_{charId:N}", CharacterRestrictions.None);
        return charId;
    }

    [Fact]
    public async Task Persistence_SiloRestart_ShouldRetainState()
    {
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        var charId = await CreateTestCharacterAsync();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
        
        var addedItem = await inventory.AddItemAsync("PersistentSword", 1);
        Assert.NotNull(addedItem);

        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        var inventoryAfterRestart = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charId, TestSeasonId);
        var hasItem = await inventoryAfterRestart.HasItemAsync(addedItem.Id);
        
        Assert.True(hasItem, "Item should exist after silo restart if persistence is working.");
    }

    [Fact]
    public async Task UserCreation_NewUser_ShouldPersistIdentity()
    {
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        var userId = Guid.NewGuid();
        var identityGrain = _cluster.GrainFactory.GetGrain<IUserIdentityGrain>(userId);
        
        await identityGrain.LinkProviderAsync("Steam", "steam_persistent_test");
        
        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        var identityGrainAfter = _cluster.GrainFactory.GetGrain<IUserIdentityGrain>(userId);
        var identity = await identityGrainAfter.GetIdentityAsync();
        
        Assert.True(identity.LinkedProviders.Any(p => p.ProviderName == "Steam"), "Should contain Steam provider");
    }

    [Fact]
    public async Task Concurrency_SimultaneousUpdates_ShouldHandleConflicts()
    {
        var userId = Guid.NewGuid();
        var userProfile = _cluster.GrainFactory.GetGrain<IUserProfileGrain>(userId);

        await userProfile.UpdateProfileAsync(new UserProfile { DisplayName = "Initial" });

        var task1 = userProfile.UpdateProfileAsync(new UserProfile { DisplayName = "Update1" });
        var task2 = userProfile.UpdateProfileAsync(new UserProfile { DisplayName = "Update2" });
        
        await Task.WhenAll(task1, task2);

        var finalProfile = await userProfile.GetProfileAsync();
        Assert.True(finalProfile.DisplayName == "Update1" || finalProfile.DisplayName == "Update2");
    }

    [Fact]
    public async Task Persistence_ActiveTrade_ShouldSurviveRestart()
    {
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        var initiatorId = await CreateTestCharacterAsync();
        var targetId = await CreateTestCharacterAsync();
        
        var initInventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(initiatorId, TestSeasonId);
        var item = await initInventory.AddItemAsync("Legendary Sword", 1);
        
        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        
        await tradeGrain.InitiateAsync(initiatorId, targetId, TestSeasonId);
        await tradeGrain.AddItemAsync(initiatorId, item.Id);

        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        var tradeGrainAfter = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        var session = await tradeGrainAfter.GetSessionAsync();
        
        Assert.Equal(TradeStatus.Pending, session.Status);
        Assert.Contains(item.Id, session.InitiatorItemIds);

        await tradeGrainAfter.AcceptAsync(initiatorId);
        await tradeGrainAfter.AcceptAsync(targetId);
        
        var sessionFinal = await tradeGrainAfter.GetSessionAsync();
        Assert.Equal(TradeStatus.Completed, sessionFinal.Status);

        var targetInventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(targetId, TestSeasonId);
        Assert.True(await targetInventory.HasItemAsync(item.Id), "Item should be transferred to target");

        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(item.Id);
        var history = await historyGrain.GetHistoryAsync();
        
        Assert.Contains(history, h => h.EventType == "Traded" && h.ActorUserId == initiatorId);
    }

    [Fact]
    public async Task Persistence_TradeStateMachine_StepByStep()
    {
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        var initiatorId = await CreateTestCharacterAsync();
        var targetId = await CreateTestCharacterAsync();
        var initInventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(initiatorId, TestSeasonId);
        var itemA = await initInventory.AddItemAsync("Item A", 1);
        var itemB = await initInventory.AddItemAsync("Item B", 1);

        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);

        await tradeGrain.InitiateAsync(initiatorId, targetId, TestSeasonId);
        await tradeGrain.AddItemAsync(initiatorId, itemA.Id);
        await tradeGrain.AcceptAsync(initiatorId);
        
        var session = await tradeGrain.GetSessionAsync();
        Assert.True(session.InitiatorAccepted, "Initiator should be accepted");
        Assert.False(session.TargetAccepted, "Target should NOT be accepted");
        Assert.Equal(TradeStatus.Pending, session.Status);

        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        var tradeGrain2 = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        var session2 = await tradeGrain2.GetSessionAsync();
        Assert.True(session2.InitiatorAccepted, "Initiator acceptance should persist");
        Assert.Single(session2.InitiatorItemIds);

        await tradeGrain2.AddItemAsync(initiatorId, itemB.Id);

        var session3 = await tradeGrain2.GetSessionAsync();
        Assert.False(session3.InitiatorAccepted, "Acceptance should reset when items change");
        Assert.Equal(2, session3.InitiatorItemIds.Count);

        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        var tradeGrain3 = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        var session4 = await tradeGrain3.GetSessionAsync();
        Assert.False(session4.InitiatorAccepted, "Reset state should persist");
        Assert.Equal(2, session4.InitiatorItemIds.Count);

        await tradeGrain3.AcceptAsync(initiatorId);
        await tradeGrain3.AcceptAsync(targetId);

        var sessionFinal = await tradeGrain3.GetSessionAsync();
        Assert.Equal(TradeStatus.Completed, sessionFinal.Status);
    }
}
