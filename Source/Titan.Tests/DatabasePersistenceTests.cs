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
}
