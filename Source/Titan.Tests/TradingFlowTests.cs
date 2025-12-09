using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Integration tests for trading flow using Orleans TestCluster.
/// Uses the permanent "standard" season for testing.
/// </summary>
public class TradingFlowTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private const string TestSeasonId = "standard";

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();

        // Ensure standard season exists
        var seasonRegistry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        await seasonRegistry.GetSeasonAsync("standard");
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task Trade_BetweenTwoCharacters_ShouldTransferItems()
    {
        // Arrange
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charA, TestSeasonId);
        var inventoryB = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charB, TestSeasonId);

        // Initialize characters
        var characterGrainA = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charA, TestSeasonId);
        var characterGrainB = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charB, TestSeasonId);
        var accountId = Guid.NewGuid();
        await characterGrainA.InitializeAsync(accountId, "CharacterA", CharacterRestrictions.None);
        await characterGrainB.InitializeAsync(accountId, "CharacterB", CharacterRestrictions.None);

        // Give each character some items
        var itemA = await inventoryA.AddItemAsync("Sword", 1);
        var itemB = await inventoryB.AddItemAsync("Shield", 1);

        // Act - Start a trade
        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);

        // Both add their items
        await tradeGrain.AddItemAsync(charA, itemA.Id);
        await tradeGrain.AddItemAsync(charB, itemB.Id);

        // Both accept
        await tradeGrain.AcceptAsync(charA);
        var finalStatus = await tradeGrain.AcceptAsync(charB);

        // Assert
        Assert.Equal(TradeStatus.Completed, finalStatus);

        // Verify items were removed from original owners
        Assert.False(await inventoryA.HasItemAsync(itemA.Id));
        Assert.False(await inventoryB.HasItemAsync(itemB.Id));

        // Verify history
        var historyA = await _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemA.Id).GetHistoryAsync();
        Assert.Contains(historyA, h => h.EventType == "Traded" && h.ActorUserId == charA && h.TargetUserId == charB);

        var historyB = await _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemB.Id).GetHistoryAsync();
        Assert.Contains(historyB, h => h.EventType == "Traded" && h.ActorUserId == charB && h.TargetUserId == charA);
    }

    [Fact]
    public async Task Trade_Gift_ShouldAllowOneSidedTransfer()
    {
        // Arrange - Gifting scenario
        var giver = Guid.NewGuid();
        var receiver = Guid.NewGuid();

        // Initialize characters
        var giverCharGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(giver, TestSeasonId);
        var receiverCharGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(receiver, TestSeasonId);
        var accountId = Guid.NewGuid();
        await giverCharGrain.InitializeAsync(accountId, "Giver", CharacterRestrictions.None);
        await receiverCharGrain.InitializeAsync(accountId, "Receiver", CharacterRestrictions.None);

        var giverInventory = _cluster.GrainFactory.GetGrain<IInventoryGrain>(giver, TestSeasonId);
        var gift = await giverInventory.AddItemAsync("GiftItem", 1);

        // Act
        var tradeId = Guid.NewGuid();
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(giver, receiver, TestSeasonId);

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
