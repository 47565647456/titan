using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for Orleans transaction support in trades.
/// Verifies atomic execution through the trade flow.
/// </summary>
public class TradeTransactionTests : IAsyncLifetime
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
    public async Task TransactionalTrade_ShouldTransferItemsAtomically()
    {
        // Arrange
        var seasonId = "transaction-test-season";
        var char1 = Guid.NewGuid();
        var char2 = Guid.NewGuid();

        var inv1 = _cluster.GrainFactory.GetGrain<IInventoryGrain>(char1, seasonId);
        var inv2 = _cluster.GrainFactory.GetGrain<IInventoryGrain>(char2, seasonId);

        // Initialize characters
        var charGrain1 = _cluster.GrainFactory.GetGrain<ICharacterGrain>(char1, seasonId);
        var charGrain2 = _cluster.GrainFactory.GetGrain<ICharacterGrain>(char2, seasonId);
        await charGrain1.InitializeAsync(Guid.NewGuid(), "Trader1", CharacterRestrictions.None);
        await charGrain2.InitializeAsync(Guid.NewGuid(), "Trader2", CharacterRestrictions.None);

        // Add items: char1 gets sword, char2 gets shield
        var sword = await inv1.AddItemAsync("sword", 1);
        var shield = await inv2.AddItemAsync("shield", 1);

        // Get initial item IDs
        var swordId = sword.Id;
        var shieldId = shield.Id;

        // Create trade
        var tradeId = Guid.NewGuid();
        var trade = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await trade.InitiateAsync(char1, char2, seasonId);

        // Add items to trade
        await trade.AddItemAsync(char1, swordId);
        await trade.AddItemAsync(char2, shieldId);

        // Act - Both accept (second triggers execution)
        await trade.AcceptAsync(char1);
        var finalStatus = await trade.AcceptAsync(char2);

        // Assert
        Assert.Equal(TradeStatus.Completed, finalStatus);

        // Verify items transferred atomically
        var inv1Items = await inv1.GetItemsAsync();
        var inv2Items = await inv2.GetItemsAsync();

        // char1 should have shield (received from char2)
        Assert.Single(inv1Items);
        Assert.Equal("shield", inv1Items[0].ItemTypeId);
        Assert.Equal(shieldId, inv1Items[0].Id);

        // char2 should have sword (received from char1)
        Assert.Single(inv2Items);
        Assert.Equal("sword", inv2Items[0].ItemTypeId);
        Assert.Equal(swordId, inv2Items[0].Id);
    }

    [Fact]
    public async Task TransactionalTrade_WithNoItems_ShouldComplete()
    {
        // Arrange - Empty trade (both parties accept with no items)
        var seasonId = "empty-trade-season";
        var char1 = Guid.NewGuid();
        var char2 = Guid.NewGuid();

        // Initialize characters
        var charGrain1 = _cluster.GrainFactory.GetGrain<ICharacterGrain>(char1, seasonId);
        var charGrain2 = _cluster.GrainFactory.GetGrain<ICharacterGrain>(char2, seasonId);
        await charGrain1.InitializeAsync(Guid.NewGuid(), "Empty1", CharacterRestrictions.None);
        await charGrain2.InitializeAsync(Guid.NewGuid(), "Empty2", CharacterRestrictions.None);

        var tradeId = Guid.NewGuid();
        var trade = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await trade.InitiateAsync(char1, char2, seasonId);

        // Act
        await trade.AcceptAsync(char1);
        var finalStatus = await trade.AcceptAsync(char2);

        // Assert
        Assert.Equal(TradeStatus.Completed, finalStatus);
    }

    [Fact]
    public async Task TransactionalTrade_MultipleItems_ShouldTransferAll()
    {
        // Arrange - Trade with multiple items on each side
        var seasonId = "multi-item-trade";
        var char1 = Guid.NewGuid();
        var char2 = Guid.NewGuid();

        var inv1 = _cluster.GrainFactory.GetGrain<IInventoryGrain>(char1, seasonId);
        var inv2 = _cluster.GrainFactory.GetGrain<IInventoryGrain>(char2, seasonId);

        // Initialize characters
        var charGrain1 = _cluster.GrainFactory.GetGrain<ICharacterGrain>(char1, seasonId);
        var charGrain2 = _cluster.GrainFactory.GetGrain<ICharacterGrain>(char2, seasonId);
        await charGrain1.InitializeAsync(Guid.NewGuid(), "Multi1", CharacterRestrictions.None);
        await charGrain2.InitializeAsync(Guid.NewGuid(), "Multi2", CharacterRestrictions.None);

        // char1 has 3 items
        var sword = await inv1.AddItemAsync("sword", 1);
        var helmet = await inv1.AddItemAsync("helmet", 1);
        var potion = await inv1.AddItemAsync("potion", 5);

        // char2 has 2 items
        var gold = await inv2.AddItemAsync("gold", 100);
        var gem = await inv2.AddItemAsync("gem", 3);

        // Create trade
        var tradeId = Guid.NewGuid();
        var trade = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await trade.InitiateAsync(char1, char2, seasonId);

        // Add all items
        await trade.AddItemAsync(char1, sword.Id);
        await trade.AddItemAsync(char1, helmet.Id);
        await trade.AddItemAsync(char1, potion.Id);
        await trade.AddItemAsync(char2, gold.Id);
        await trade.AddItemAsync(char2, gem.Id);

        // Act
        await trade.AcceptAsync(char1);
        var finalStatus = await trade.AcceptAsync(char2);

        // Assert
        Assert.Equal(TradeStatus.Completed, finalStatus);

        var inv1Items = await inv1.GetItemsAsync();
        var inv2Items = await inv2.GetItemsAsync();

        // char1 should now have char2's items (gold, gem)
        Assert.Equal(2, inv1Items.Count);
        Assert.Contains(inv1Items, i => i.ItemTypeId == "gold");
        Assert.Contains(inv1Items, i => i.ItemTypeId == "gem");

        // char2 should now have char1's items (sword, helmet, potion)
        Assert.Equal(3, inv2Items.Count);
        Assert.Contains(inv2Items, i => i.ItemTypeId == "sword");
        Assert.Contains(inv2Items, i => i.ItemTypeId == "helmet");
        Assert.Contains(inv2Items, i => i.ItemTypeId == "potion");
    }

    [Fact]
    public async Task TransactionalTrade_Gift_OneSidedTransfer()
    {
        // Arrange - One party gives items, other gives nothing
        var seasonId = "gift-trade";
        var giver = Guid.NewGuid();
        var receiver = Guid.NewGuid();

        var giverInv = _cluster.GrainFactory.GetGrain<IInventoryGrain>(giver, seasonId);
        var receiverInv = _cluster.GrainFactory.GetGrain<IInventoryGrain>(receiver, seasonId);

        // Initialize characters
        var charGiver = _cluster.GrainFactory.GetGrain<ICharacterGrain>(giver, seasonId);
        var charReceiver = _cluster.GrainFactory.GetGrain<ICharacterGrain>(receiver, seasonId);
        await charGiver.InitializeAsync(Guid.NewGuid(), "Giver", CharacterRestrictions.None);
        await charReceiver.InitializeAsync(Guid.NewGuid(), "Receiver", CharacterRestrictions.None);

        var gift = await giverInv.AddItemAsync("rare_artifact", 1);

        // Create trade
        var tradeId = Guid.NewGuid();
        var trade = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await trade.InitiateAsync(giver, receiver, seasonId);

        // Only giver adds item
        await trade.AddItemAsync(giver, gift.Id);

        // Act
        await trade.AcceptAsync(giver);
        var finalStatus = await trade.AcceptAsync(receiver);

        // Assert
        Assert.Equal(TradeStatus.Completed, finalStatus);

        var giverItems = await giverInv.GetItemsAsync();
        var receiverItems = await receiverInv.GetItemsAsync();

        Assert.Empty(giverItems); // Giver lost their item
        Assert.Single(receiverItems);
        Assert.Equal("rare_artifact", receiverItems[0].ItemTypeId);
    }
}


