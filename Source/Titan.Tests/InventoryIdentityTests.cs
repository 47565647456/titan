using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Integration tests for Inventory and Identity grains using Orleans TestCluster.
/// </summary>
public class InventoryIdentityTests : IAsyncLifetime
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

    [Fact]
    public async Task Inventory_AddAndRemoveItems_ShouldWork()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var inventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(characterId, TestSeasonId);

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
