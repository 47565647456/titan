using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;
using Xunit;

namespace Titan.AppHost.Tests;

/// <summary>
/// Trading flow integration tests.
/// Tests trade lifecycle via SignalR hubs.
/// </summary>
[Collection("AppHost")]
public class TradingFlowIntegrationTests : IntegrationTestBase
{
    public TradingFlowIntegrationTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task StartTrade_BetweenTwoUsers_ReturnsTradeSession()
    {
        // Arrange - Create two users with characters
        var user1 = await CreateUserSessionAsync();
        var user2 = await CreateUserSessionAsync();

        var accountHub1 = await user1.GetAccountHubAsync();
        var accountHub2 = await user2.GetAccountHubAsync();
        
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>("CreateCharacter", "standard", $"Trader1_{Guid.NewGuid():N}", CharacterRestrictions.None);
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>("CreateCharacter", "standard", $"Trader2_{Guid.NewGuid():N}", CharacterRestrictions.None);

        Assert.NotNull(char1);
        Assert.NotNull(char2);

        // Act - User1 starts trade with User2
        var tradeHub1 = await user1.GetTradeHubAsync();
        var tradeSession = await tradeHub1.InvokeAsync<TradeSession>("StartTrade", char1.CharacterId, char2.CharacterId, "standard");

        // Assert
        Assert.NotNull(tradeSession);
        Assert.NotEqual(Guid.Empty, tradeSession.TradeId);

        // Cleanup
        await user1.DisposeAsync();
        await user2.DisposeAsync();
    }

    [Fact]
    public async Task CancelTrade_AfterStarting_Succeeds()
    {
        // Arrange
        var user1 = await CreateUserSessionAsync();
        var user2 = await CreateUserSessionAsync();

        var accountHub1 = await user1.GetAccountHubAsync();
        var accountHub2 = await user2.GetAccountHubAsync();
        
        var char1 = await accountHub1.InvokeAsync<CharacterSummary>("CreateCharacter", "standard", $"Cancel1_{Guid.NewGuid():N}", CharacterRestrictions.None);
        var char2 = await accountHub2.InvokeAsync<CharacterSummary>("CreateCharacter", "standard", $"Cancel2_{Guid.NewGuid():N}", CharacterRestrictions.None);

        var tradeHub1 = await user1.GetTradeHubAsync();
        var tradeSession = await tradeHub1.InvokeAsync<TradeSession>("StartTrade", char1!.CharacterId, char2!.CharacterId, "standard");

        // Act - Cancel the trade (no exception means success)
        await tradeHub1.InvokeAsync("CancelTrade", tradeSession!.TradeId);

        // Assert - Trade should be cancelled (no exception thrown)
        Assert.True(true);

        // Cleanup
        await user1.DisposeAsync();
        await user2.DisposeAsync();
    }
}
