using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Xunit;
using System.Linq;

namespace Titan.Tests;

/// <summary>
/// Tests specifically focused on database persistence and concurrency guarantees.
/// Requires USE_DATABASE=true environment variable to be meaningful, otherwise runs against memory.
/// </summary>
public class DatabasePersistenceTests : IAsyncLifetime
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
    public async Task Persistence_SiloRestart_ShouldRetainState()
    {
        // 1. Arrange: Create data in the "first" cluster life
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true")
        {
            // Skip test if no database is configured
            return;
        }

        var userId = Guid.NewGuid();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
        
        var addedItem = await inventory.AddItemAsync("PersistentSword", 1);
        Assert.NotNull(addedItem);

        // 2. Act: Restart the silo (simulating crash/redeploy)
        // Note: In TestCluster, we can't easily "crash" and keep memory state, 
        // but stopping and starting silos with a Persistent store configured (Cockroach) will prove it.
        // For a true "restart" preserving the same cluster ID, it's complex in TestCluster. 
        // A simpler proxy is: The TestCluster creates a Real database connection. 
        // Ideally, we'd dispose the entire cluster and recreate it pointing to the SAME cleanup-less DB.
        // However, standard TestCluster flows usually wipe state if it's in-memory. 
        // If we are using CockroachDB (external), creating a NEW TestCluster (simulating a new Silo) 
        // that connects to the SAME DB string would verify persistence.
        
        // Let's try to just Stop and Start the secondary silo if we had multiple, but we have one.
        // We will manually stop and restart the cluster. 
        await _cluster.StopAllSilosAsync();
        
        // Re-deploy (restart)
        // Ideally this re-joins or starts fresh. 
        // NOTE: If TestCluster disposal wipes the DB, this fails. 
        // But usually AdoNet storage doesn't auto-wipe external DBs on Stop, only In-Memory does.
        await _cluster.DeployAsync();

        // 3. Assert: Read data back
        var inventoryAfterRestart = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userId);
        var hasItem = await inventoryAfterRestart.HasItemAsync(addedItem.Id);
        
        Assert.True(hasItem, "Item should exist after silo restart if persistence is working.");
    }

    [Fact]
    public async Task UserCreation_NewUser_ShouldPersistIdentity()
    {
        // 1. Arrange: Create a new user identity
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        var userId = Guid.NewGuid();
        var identityGrain = _cluster.GrainFactory.GetGrain<IUserIdentityGrain>(userId);
        
        await identityGrain.LinkProviderAsync("Steam", "steam_persistent_test");
        
        // 2. Act: Restart Cluster
        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        // 3. Assert: Check if identity persists
        var identityGrainAfter = _cluster.GrainFactory.GetGrain<IUserIdentityGrain>(userId);
        var identity = await identityGrainAfter.GetIdentityAsync();
        
        Assert.True(identity.LinkedProviders.Any(p => p.ProviderName == "Steam"), "Should contain Steam provider");
    }

    [Fact]
    public async Task Concurrency_SimultaneousUpdates_ShouldHandleConflicts()
    {
        // 1. Arrange
        var userId = Guid.NewGuid();
        var userProfile = _cluster.GrainFactory.GetGrain<IUserProfileGrain>(userId);

        // Initial write
        await userProfile.UpdateProfileAsync(new UserProfile { DisplayName = "Initial" });

        // 2. Act: Read same grain from two "clients" (or just two tasks) and try to update.
        // Note: Orleans grains process messages one-at-a-time (Turn-Based Concurrency).
        // This test verifies that we don't get "Lost Updates" if the logic reads-then-writes.
        // However, to test *Storage* concurrency (ETags), calls normally serialize in the grain.
        // A true conflict usually happens if we have Reentrancy or external updates. 
        // For standard transaction/state grains, Orleans ensures consistency.
        
        // Let's try a race condition simulation if the Grain logic allows interleaving, 
        // or just verify that simple rapid updates don't corrupt state.
        
        var task1 = userProfile.UpdateProfileAsync(new UserProfile { DisplayName = "Update1" });
        var task2 = userProfile.UpdateProfileAsync(new UserProfile { DisplayName = "Update2" });
        
        await Task.WhenAll(task1, task2);

        // 3. Assert
        var finalProfile = await userProfile.GetProfileAsync();
        Assert.True(finalProfile.DisplayName == "Update1" || finalProfile.DisplayName == "Update2");
        // It shouldn't be "Initial" or corrupted.
    }
    [Fact]
    public async Task Persistence_ActiveTrade_ShouldSurviveRestart()
    {
        // 0. Guard Clause
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        // 1. Arrange: Setup Users and Items
        var initiatorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        
        // Create Item for Initiator
        var initInventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(initiatorId);
        var item = await initInventory.AddItemAsync("Legendary Sword", 1);
        
        // 2. Arrange: Initiate Trade and key state
        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        
        await tradeGrain.InitiateAsync(initiatorId, targetId);
        await tradeGrain.AddItemAsync(initiatorId, item.Id);

        // 3. Act: Restart Silo (Simulate Crash mid-trade)
        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        // 4. Assert: Verify Trade State Persisted (Pending, Items present)
        var tradeGrainAfter = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        var session = await tradeGrainAfter.GetSessionAsync();
        
        Assert.Equal(TradeStatus.Pending, session.Status);
        Assert.Contains(item.Id, session.InitiatorItemIds);

        // 5. Act: Complete Trade (Resume workflow)
        await tradeGrainAfter.AcceptAsync(initiatorId);
        await tradeGrainAfter.AcceptAsync(targetId);
        
        // 6. Assert: Verify Completion and Transfer
        var sessionFinal = await tradeGrainAfter.GetSessionAsync();
        Assert.Equal(TradeStatus.Completed, sessionFinal.Status);

        var targetInventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(targetId);
        Assert.True(await targetInventory.HasItemAsync(item.Id), "Item should be transferred to target");

        // 7. Assert: Verify History (Audit Log) is written and retrievable
        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(item.Id);
        var history = await historyGrain.GetHistoryAsync();
        
        Assert.Contains(history, h => h.EventType == "Traded" && h.ActorUserId == initiatorId);
    }

    [Fact]
    public async Task Persistence_TradeStateMachine_StepByStep()
    {
        // 0. Guard Clause
        if (Environment.GetEnvironmentVariable("USE_DATABASE") != "true") return;

        // 1. Arrange: Setup Users and Items
        var initiatorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var initInventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(initiatorId);
        var itemA = await initInventory.AddItemAsync("Item A", 1);
        var itemB = await initInventory.AddItemAsync("Item B", 1);

        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);

        // 2. Act: Initiate
        await tradeGrain.InitiateAsync(initiatorId, targetId);

        // 3. Act: Add Item A
        await tradeGrain.AddItemAsync(initiatorId, itemA.Id);
        
        // 4. Act: Initiator Accepts
        await tradeGrain.AcceptAsync(initiatorId);
        
        // Assert: Partial Acceptance
        var session = await tradeGrain.GetSessionAsync();
        Assert.True(session.InitiatorAccepted, "Initiator should be accepted");
        Assert.False(session.TargetAccepted, "Target should NOT be accepted");
        Assert.Equal(TradeStatus.Pending, session.Status);

        // 5. Act: Restart to verify Partial Acceptance Persistence
        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        // Reload Grain
        var tradeGrain2 = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        var session2 = await tradeGrain2.GetSessionAsync();
        Assert.True(session2.InitiatorAccepted, "Initiator acceptance should persist");
        Assert.Single(session2.InitiatorItemIds);

        // 6. Act: Add Item B (Should Reset Acceptance)
        await tradeGrain2.AddItemAsync(initiatorId, itemB.Id);

        // Assert: Reset Logic
        var session3 = await tradeGrain2.GetSessionAsync();
        Assert.False(session3.InitiatorAccepted, "Acceptance should reset when items change");
        Assert.Equal(2, session3.InitiatorItemIds.Count);

        // 7. Act: Restart again (Verify Reset Persistence)
        await _cluster.StopAllSilosAsync();
        await _cluster.DeployAsync();

        var tradeGrain3 = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        var session4 = await tradeGrain3.GetSessionAsync();
        Assert.False(session4.InitiatorAccepted, "Reset state should persist");
        Assert.Equal(2, session4.InitiatorItemIds.Count);

        // 8. Act: Complete Trade
        await tradeGrain3.AcceptAsync(initiatorId);
        await tradeGrain3.AcceptAsync(targetId);

        // Assert: Final
        var sessionFinal = await tradeGrain3.GetSessionAsync();
        Assert.Equal(TradeStatus.Completed, sessionFinal.Status);
    }
}
