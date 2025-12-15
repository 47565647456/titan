using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;

namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for trading functionality.
/// Note: Trading tests require items to be generated via IItemGeneratorGrain, 
/// which is not exposed via SignalR. These tests focus on trade session lifecycle.
/// </summary>
[Collection("AppHost")]
public class TradingTests : IntegrationTestBase
{
    public TradingTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Trade_SessionLifecycle_Works()
    {
        // Arrange - Two users with characters
        await using var user1 = await CreateUserSessionAsync();
        await using var user2 = await CreateUserSessionAsync();

        // Create characters
        var accountHub1 = await user1.GetAccountHubAsync();
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "Trader1", CharacterRestrictions.None);

        var accountHub2 = await user2.GetAccountHubAsync();
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "Trader2", CharacterRestrictions.None);

        // Act - Start trade
        var tradeHub1 = await user1.GetTradeHubAsync();
        var session = await tradeHub1.InvokeAsync<TradeSession>(
            "StartTrade", char1.CharacterId, char2.CharacterId, "standard");

        // Assert - Trade session created
        Assert.NotNull(session);
        Assert.Equal(TradeStatus.Pending, session.Status);
        Assert.Equal(char1.CharacterId, session.InitiatorCharacterId);
        Assert.Equal(char2.CharacterId, session.TargetCharacterId);

        // Cancel the trade
        await tradeHub1.InvokeAsync("CancelTrade", session.TradeId);
    }

    [Fact]
    public async Task Trade_BothAccept_WithNoItems_CompletesSuccessfully()
    {
        // Arrange - Two users with characters
        await using var user1 = await CreateUserSessionAsync();
        await using var user2 = await CreateUserSessionAsync();

        // Create characters
        var accountHub1 = await user1.GetAccountHubAsync();
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "EmptyTrader1", CharacterRestrictions.None);

        var accountHub2 = await user2.GetAccountHubAsync();
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "EmptyTrader2", CharacterRestrictions.None);

        // Act - Start trade with no items
        var tradeHub1 = await user1.GetTradeHubAsync();
        var session = await tradeHub1.InvokeAsync<TradeSession>(
            "StartTrade", char1.CharacterId, char2.CharacterId, "standard");

        // Accept trade (no items)
        await tradeHub1.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

        var tradeHub2 = await user2.GetTradeHubAsync();
        var result = await tradeHub2.InvokeAsync<AcceptTradeResult>("AcceptTrade", session.TradeId);

        // Assert
        Assert.True(result.Completed);
        Assert.Equal(TradeStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Trade_CanBeCancelled()
    {
        // Arrange
        await using var user1 = await CreateUserSessionAsync();
        await using var user2 = await CreateUserSessionAsync();

        var accountHub1 = await user1.GetAccountHubAsync();
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "Canceller", CharacterRestrictions.None);

        var accountHub2 = await user2.GetAccountHubAsync();
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "CancelTarget", CharacterRestrictions.None);

        // Start trade
        var tradeHub1 = await user1.GetTradeHubAsync();
        var session = await tradeHub1.InvokeAsync<TradeSession>(
            "StartTrade", char1.CharacterId, char2.CharacterId, "standard");

        // Act - Cancel the trade
        await tradeHub1.InvokeAsync("CancelTrade", session.TradeId);

        // Assert - Getting session shows cancelled status
        var cancelledSession = await tradeHub1.InvokeAsync<TradeSession>("GetTrade", session.TradeId);
        Assert.Equal(TradeStatus.Cancelled, cancelledSession.Status);
    }

    [Fact]
    public async Task SSF_CharacterCannotTrade()
    {
        // Arrange
        await using var user1 = await CreateUserSessionAsync();
        await using var user2 = await CreateUserSessionAsync();

        // Create SSF character
        var accountHub1 = await user1.GetAccountHubAsync();
        var ssfChar = await accountHub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "SSFPlayer", CharacterRestrictions.SoloSelfFound);

        var accountHub2 = await user2.GetAccountHubAsync();
        var normalChar = await accountHub2.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "NormalPlayer", CharacterRestrictions.None);

        // Act & Assert - SSF character cannot initiate trade
        var tradeHub1 = await user1.GetTradeHubAsync();
        await Assert.ThrowsAsync<HubException>(() =>
            tradeHub1.InvokeAsync<TradeSession>(
                "StartTrade", ssfChar.CharacterId, normalChar.CharacterId, "standard"));
    }
}

public record AcceptTradeResult(TradeStatus Status, bool Completed);
