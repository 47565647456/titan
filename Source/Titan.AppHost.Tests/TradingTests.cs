using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;

namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for trading functionality.
/// </summary>
[Collection("AppHost")]
public class TradingTests : IntegrationTestBase
{
    public TradingTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Trade_CompletesSuccessfully()
    {
        // Arrange - Two users with characters and items
        var (token1, _) = await LoginAsUserAsync();
        var (token2, _) = await LoginAsUserAsync();
        var (adminToken, _) = await LoginAsAdminAsync();

        // Register required item types first
        await EnsureItemTypeExistsAsync(adminToken, "sword", isTradeable: true);
        await EnsureItemTypeExistsAsync(adminToken, "shield", isTradeable: true);

        // Create characters
        var accountHub1 = await ConnectToHubAsync("/accountHub", token1);
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "Trader1", CharacterRestrictions.None);
        await accountHub1.DisposeAsync();

        var accountHub2 = await ConnectToHubAsync("/accountHub", token2);
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "Trader2", CharacterRestrictions.None);
        await accountHub2.DisposeAsync();

        // Add items to each character
        var invHub1 = await ConnectToHubAsync("/inventoryHub", token1);
        var sword = await invHub1.InvokeAsync<Item>(
            "AddItem", char1.CharacterId, "standard", "sword", 1, (Dictionary<string, object>?)null);
        await invHub1.DisposeAsync();

        var invHub2 = await ConnectToHubAsync("/inventoryHub", token2);
        var shield = await invHub2.InvokeAsync<Item>(
            "AddItem", char2.CharacterId, "standard", "shield", 1, (Dictionary<string, object>?)null);
        await invHub2.DisposeAsync();

        // Act - Start trade
        var tradeHub1 = await ConnectToHubAsync("/tradeHub", token1);
        var session = await tradeHub1.InvokeAsync<TradeSession>(
            "StartTrade", char1.CharacterId, char2.CharacterId, "standard");

        // Add items
        await tradeHub1.InvokeAsync<TradeSession>("AddItem", session.TradeId, sword.Id);

        var tradeHub2 = await ConnectToHubAsync("/tradeHub", token2);
        await tradeHub2.InvokeAsync<TradeSession>("AddItem", session.TradeId, shield.Id);

        // Accept trade
        await tradeHub1.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);
        var result = await tradeHub2.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

        // Assert
        Assert.True(result.Completed);
        Assert.Equal(TradeStatus.Completed, result.Status);
        
        await tradeHub1.DisposeAsync();
        await tradeHub2.DisposeAsync();
    }

    [Fact]
    public async Task Trade_ItemsTransferAtomically()
    {
        // Arrange
        var (token1, _) = await LoginAsUserAsync();
        var (token2, _) = await LoginAsUserAsync();
        var (adminToken, _) = await LoginAsAdminAsync();

        // Register required item type
        await EnsureItemTypeExistsAsync(adminToken, "rare_gem", isTradeable: true);

        // Create characters
        var accountHub1 = await ConnectToHubAsync("/accountHub", token1);
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "AtomicTrader1", CharacterRestrictions.None);
        await accountHub1.DisposeAsync();

        var accountHub2 = await ConnectToHubAsync("/accountHub", token2);
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "AtomicTrader2", CharacterRestrictions.None);
        await accountHub2.DisposeAsync();

        // Give char1 an item
        var invHub1 = await ConnectToHubAsync("/inventoryHub", token1);
        var item = await invHub1.InvokeAsync<Item>(
            "AddItem", char1.CharacterId, "standard", "rare_gem", 1, (Dictionary<string, object>?)null);
        
        // Execute trade
        var tradeHub1 = await ConnectToHubAsync("/tradeHub", token1);
        var session = await tradeHub1.InvokeAsync<TradeSession>(
            "StartTrade", char1.CharacterId, char2.CharacterId, "standard");
        await tradeHub1.InvokeAsync<TradeSession>("AddItem", session.TradeId, item.Id);
        await tradeHub1.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

        var tradeHub2 = await ConnectToHubAsync("/tradeHub", token2);
        await tradeHub2.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

        // Assert - Item moved from char1 to char2
        var char1Items = await invHub1.InvokeAsync<IReadOnlyList<Item>>(
            "GetInventory", char1.CharacterId, "standard");
        Assert.Empty(char1Items);

        var invHub2 = await ConnectToHubAsync("/inventoryHub", token2);
        var char2Items = await invHub2.InvokeAsync<IReadOnlyList<Item>>(
            "GetInventory", char2.CharacterId, "standard");
        Assert.Single(char2Items);
        Assert.Equal(item.Id, char2Items[0].Id);
        
        await invHub1.DisposeAsync();
        await invHub2.DisposeAsync();
        await tradeHub1.DisposeAsync();
        await tradeHub2.DisposeAsync();
    }

    [Fact]
    public async Task User_CanOnlyAddOwnItems()
    {
        // Arrange
        var (token1, _) = await LoginAsUserAsync();
        var (token2, _) = await LoginAsUserAsync();
        var (adminToken, _) = await LoginAsAdminAsync();

        // Register required item type
        await EnsureItemTypeExistsAsync(adminToken, "stolen_goods", isTradeable: true);

        // Create characters
        var accountHub1 = await ConnectToHubAsync("/accountHub", token1);
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "Owner", CharacterRestrictions.None);
        await accountHub1.DisposeAsync();

        var accountHub2 = await ConnectToHubAsync("/accountHub", token2);
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "OtherUser", CharacterRestrictions.None);
        await accountHub2.DisposeAsync();

        // User2 creates an item
        var invHub2 = await ConnectToHubAsync("/inventoryHub", token2);
        var user2Item = await invHub2.InvokeAsync<Item>(
            "AddItem", char2.CharacterId, "standard", "stolen_goods", 1, (Dictionary<string, object>?)null);
        await invHub2.DisposeAsync();

        // Start trade between the characters (initiated by user1)
        var tradeHub1 = await ConnectToHubAsync("/tradeHub", token1);
        var session = await tradeHub1.InvokeAsync<TradeSession>(
            "StartTrade", char1.CharacterId, char2.CharacterId, "standard");

        // Act & Assert - User1 tries to add User2's item (should fail)
        await Assert.ThrowsAsync<HubException>(() => 
            tradeHub1.InvokeAsync<TradeSession>("AddItem", session.TradeId, user2Item.Id));
        
        await tradeHub1.DisposeAsync();
    }

    /// <summary>
    /// Stress test: Multiple concurrent trades to verify CockroachDB handles serializable transactions.
    /// Each trade uses GlobalStorage which connects to CockroachDB in a multi-region setup.
    /// Uses batching to avoid connection exhaustion while still testing high volume.
    /// </summary>
    [Fact]
    public async Task ConcurrentTrades_StressTest()
    {
        // Arrange - Create multiple user pairs
        // Note: Reduced from 1000 to avoid Windows ephemeral port exhaustion
        // Each trade creates ~8 SignalR connections, and Windows has limited ephemeral ports
        const int numTradePairs = 200;
        const int batchSize = 20;  // Smaller batches to avoid socket exhaustion
        var (adminToken, _) = await LoginAsAdminAsync();
        
        // Register tradeable item type
        await EnsureItemTypeExistsAsync(adminToken, "stress_item", isTradeable: true);

        var allResults = new List<bool>();

        // Process trades in batches
        for (int batchStart = 0; batchStart < numTradePairs; batchStart += batchSize)
        {
            var currentBatchSize = Math.Min(batchSize, numTradePairs - batchStart);
            var batchTasks = new List<Task<bool>>();

            for (int i = 0; i < currentBatchSize; i++)
            {
                var pairIndex = batchStart + i;
                batchTasks.Add(ExecuteSingleTradeAsync(pairIndex));
            }

            // Wait for this batch to complete before starting the next
            var batchResults = await Task.WhenAll(batchTasks);
            allResults.AddRange(batchResults);
            
            // Longer delay between batches to let connections fully close (TIME_WAIT cleanup)
            if (batchStart + batchSize < numTradePairs)
            {
                await Task.Delay(500);
            }
        }

        // Assert - With retry logic and batching, most trades should complete (90%+ success)
        var successCount = allResults.Count(r => r);
        var successRate = (double)successCount / numTradePairs;
        Assert.True(successRate >= 0.90, 
            $"Expected at least 90% success rate, got {successRate:P1} ({successCount}/{numTradePairs})");
    }

    private async Task<bool> ExecuteSingleTradeAsync(int pairIndex)
    {
        try
        {
            // Each pair has their own users
            var (token1, _) = await LoginAsUserAsync();
            var (token2, _) = await LoginAsUserAsync();

            // Create characters
            var accountHub1 = await ConnectToHubAsync("/accountHub", token1);
            var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
                "CreateCharacter", "standard", $"StressTrader{pairIndex}A", CharacterRestrictions.None);
            await accountHub1.DisposeAsync();

            var accountHub2 = await ConnectToHubAsync("/accountHub", token2);
            var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
                "CreateCharacter", "standard", $"StressTrader{pairIndex}B", CharacterRestrictions.None);
            await accountHub2.DisposeAsync();

            // Give both characters items
            var invHub1 = await ConnectToHubAsync("/inventoryHub", token1);
            var item1 = await invHub1.InvokeAsync<Item>(
                "AddItem", char1.CharacterId, "standard", "stress_item", 1, (Dictionary<string, object>?)null);
            await invHub1.DisposeAsync();

            var invHub2 = await ConnectToHubAsync("/inventoryHub", token2);
            var item2 = await invHub2.InvokeAsync<Item>(
                "AddItem", char2.CharacterId, "standard", "stress_item", 1, (Dictionary<string, object>?)null);
            await invHub2.DisposeAsync();

            // Execute trade
            var tradeHub1 = await ConnectToHubAsync("/tradeHub", token1);
            var session = await tradeHub1.InvokeAsync<TradeSession>(
                "StartTrade", char1.CharacterId, char2.CharacterId, "standard");
            await tradeHub1.InvokeAsync<TradeSession>("AddItem", session.TradeId, item1.Id);

            var tradeHub2 = await ConnectToHubAsync("/tradeHub", token2);
            await tradeHub2.InvokeAsync<TradeSession>("AddItem", session.TradeId, item2.Id);

            // Both accept
            await tradeHub1.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);
            var result = await tradeHub2.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

            await tradeHub1.DisposeAsync();
            await tradeHub2.DisposeAsync();

            return result.Completed && result.Status == TradeStatus.Completed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Trade pair {pairIndex} failed: {ex.Message}");
            return false;
        }
    }
}

public record AcceptTradeResult(TradeStatus Status, bool Completed);
