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
        await using var user1 = await CreateUserSessionAsync();
        await using var user2 = await CreateUserSessionAsync();
        await using var admin = await CreateAdminSessionAsync();

        // Register required item types first
        await EnsureItemTypeExistsAsync(admin, "sword", isTradeable: true);
        await EnsureItemTypeExistsAsync(admin, "shield", isTradeable: true);

        // Create characters
        var accountHub1 = await user1.GetAccountHubAsync();
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "Trader1", CharacterRestrictions.None);

        var accountHub2 = await user2.GetAccountHubAsync();
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "Trader2", CharacterRestrictions.None);

        // Add items to each character
        var invHub1 = await user1.GetInventoryHubAsync();
        var sword = await invHub1.InvokeAsync<Item>(
            "AddItem", char1.CharacterId, "standard", "sword", 1, (Dictionary<string, object>?)null);

        var invHub2 = await user2.GetInventoryHubAsync();
        var shield = await invHub2.InvokeAsync<Item>(
            "AddItem", char2.CharacterId, "standard", "shield", 1, (Dictionary<string, object>?)null);

        // Act - Start trade
        var tradeHub1 = await user1.GetTradeHubAsync();
        var session = await tradeHub1.InvokeAsync<TradeSession>(
            "StartTrade", char1.CharacterId, char2.CharacterId, "standard");

        // Add items
        await tradeHub1.InvokeAsync<TradeSession>("AddItem", session.TradeId, sword.Id);

        var tradeHub2 = await user2.GetTradeHubAsync();
        await tradeHub2.InvokeAsync<TradeSession>("AddItem", session.TradeId, shield.Id);

        // Accept trade
        await tradeHub1.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);
        var result = await tradeHub2.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

        // Assert
        Assert.True(result.Completed);
        Assert.Equal(TradeStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Trade_ItemsTransferAtomically()
    {
        // Arrange
        await using var user1 = await CreateUserSessionAsync();
        await using var user2 = await CreateUserSessionAsync();
        await using var admin = await CreateAdminSessionAsync();

        // Register required item type
        await EnsureItemTypeExistsAsync(admin, "rare_gem", isTradeable: true);

        // Create characters
        var accountHub1 = await user1.GetAccountHubAsync();
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "AtomicTrader1", CharacterRestrictions.None);

        var accountHub2 = await user2.GetAccountHubAsync();
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "AtomicTrader2", CharacterRestrictions.None);

        // Give char1 an item
        var invHub1 = await user1.GetInventoryHubAsync();
        var item = await invHub1.InvokeAsync<Item>(
            "AddItem", char1.CharacterId, "standard", "rare_gem", 1, (Dictionary<string, object>?)null);
        
        // Execute trade
        var tradeHub1 = await user1.GetTradeHubAsync();
        var session = await tradeHub1.InvokeAsync<TradeSession>(
            "StartTrade", char1.CharacterId, char2.CharacterId, "standard");
        await tradeHub1.InvokeAsync<TradeSession>("AddItem", session.TradeId, item.Id);
        await tradeHub1.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

        var tradeHub2 = await user2.GetTradeHubAsync();
        await tradeHub2.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

        // Assert - Item moved from char1 to char2
        var char1Items = await invHub1.InvokeAsync<IReadOnlyList<Item>>(
            "GetInventory", char1.CharacterId, "standard");
        Assert.Empty(char1Items);

        var invHub2 = await user2.GetInventoryHubAsync();
        var char2Items = await invHub2.InvokeAsync<IReadOnlyList<Item>>(
            "GetInventory", char2.CharacterId, "standard");
        Assert.Single(char2Items);
        Assert.Equal(item.Id, char2Items[0].Id);
    }

    [Fact]
    public async Task User_CanOnlyAddOwnItems()
    {
        // Arrange
        await using var user1 = await CreateUserSessionAsync();
        await using var user2 = await CreateUserSessionAsync();
        await using var admin = await CreateAdminSessionAsync();

        // Register required item type
        await EnsureItemTypeExistsAsync(admin, "stolen_goods", isTradeable: true);

        // Create characters
        var accountHub1 = await user1.GetAccountHubAsync();
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "Owner", CharacterRestrictions.None);

        var accountHub2 = await user2.GetAccountHubAsync();
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "OtherUser", CharacterRestrictions.None);

        // User2 creates an item
        var invHub2 = await user2.GetInventoryHubAsync();
        var user2Item = await invHub2.InvokeAsync<Item>(
            "AddItem", char2.CharacterId, "standard", "stolen_goods", 1, (Dictionary<string, object>?)null);

        // Start trade between the characters (initiated by user1)
        var tradeHub1 = await user1.GetTradeHubAsync();
        var session = await tradeHub1.InvokeAsync<TradeSession>(
            "StartTrade", char1.CharacterId, char2.CharacterId, "standard");

        // Act & Assert - User1 tries to add User2's item (should fail)
        await Assert.ThrowsAsync<HubException>(() => 
            tradeHub1.InvokeAsync<TradeSession>("AddItem", session.TradeId, user2Item.Id));
    }

    /// <summary>
    /// Stress test: Multiple concurrent trades to verify CockroachDB handles serializable transactions.
    /// Each trade uses GlobalStorage which connects to CockroachDB in a multi-region setup.
    /// Uses batching to avoid connection exhaustion while still testing high volume.
    /// Now uses UserSession to dramatically reduce connection count.
    /// </summary>
    [Fact]
    public async Task ConcurrentTrades_StressTest()
    {
        // Arrange - Now uses fewer connections thanks to UserSession pattern
        // Each trade pair now uses ~3 sessions (2 users + 1 shared admin) vs ~8 connections before
        const int numTradePairs = 200;
        const int batchSize = 20;
        await using var admin = await CreateAdminSessionAsync();
        
        // Register tradeable item type
        await EnsureItemTypeExistsAsync(admin, "stress_item", isTradeable: true);

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
            
            // Shorter delay now that we have fewer connections to clean up
            if (batchStart + batchSize < numTradePairs)
            {
                await Task.Delay(200);
            }
        }

        // Assert - With UserSession pattern, success rate should be higher
        var successCount = allResults.Count(r => r);
        var successRate = (double)successCount / numTradePairs;
        Assert.True(successRate >= 0.90, 
            $"Expected at least 90% success rate, got {successRate:P1} ({successCount}/{numTradePairs})");
    }

    private async Task<bool> ExecuteSingleTradeAsync(int pairIndex)
    {
        try
        {
            // Each pair has their own sessions (connections reused within session)
            await using var user1 = await CreateUserSessionAsync();
            await using var user2 = await CreateUserSessionAsync();

            // Create characters - reuses account hub connection
            var accountHub1 = await user1.GetAccountHubAsync();
            var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
                "CreateCharacter", "standard", $"StressTrader{pairIndex}A", CharacterRestrictions.None);

            var accountHub2 = await user2.GetAccountHubAsync();
            var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
                "CreateCharacter", "standard", $"StressTrader{pairIndex}B", CharacterRestrictions.None);

            // Give both characters items - reuses inventory hub connection
            var invHub1 = await user1.GetInventoryHubAsync();
            var item1 = await invHub1.InvokeAsync<Item>(
                "AddItem", char1.CharacterId, "standard", "stress_item", 1, (Dictionary<string, object>?)null);

            var invHub2 = await user2.GetInventoryHubAsync();
            var item2 = await invHub2.InvokeAsync<Item>(
                "AddItem", char2.CharacterId, "standard", "stress_item", 1, (Dictionary<string, object>?)null);

            // Execute trade - reuses trade hub connection
            var tradeHub1 = await user1.GetTradeHubAsync();
            var session = await tradeHub1.InvokeAsync<TradeSession>(
                "StartTrade", char1.CharacterId, char2.CharacterId, "standard");
            await tradeHub1.InvokeAsync<TradeSession>("AddItem", session.TradeId, item1.Id);

            var tradeHub2 = await user2.GetTradeHubAsync();
            await tradeHub2.InvokeAsync<TradeSession>("AddItem", session.TradeId, item2.Id);

            // Both accept
            await tradeHub1.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);
            var result = await tradeHub2.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

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
