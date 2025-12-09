using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for trade expiration functionality.
/// Uses a 5-second timeout configured in TestSiloConfigurator.
/// </summary>
public class TradeExpirationTests : IAsyncLifetime
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
    public async Task PendingTrade_ShouldExpire_AfterTimeout()
    {
        // Arrange
        var charA = await CreateTestCharacterAsync();
        var charB = await CreateTestCharacterAsync();
        var tradeId = Guid.NewGuid();

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);

        // Verify it's pending
        var session = await tradeGrain.GetSessionAsync();
        Assert.Equal(TradeStatus.Pending, session.Status);

        // Act - Wait for expiration (5 seconds timeout + buffer)
        await Task.Delay(TimeSpan.FromSeconds(7));

        // Assert - Should now be expired
        var expiredSession = await tradeGrain.GetSessionAsync();
        Assert.Equal(TradeStatus.Expired, expiredSession.Status);
    }

    [Fact]
    public async Task CompletedTrade_ShouldNotExpire()
    {
        // Arrange - Complete a trade quickly
        var charA = await CreateTestCharacterAsync();
        var charB = await CreateTestCharacterAsync();
        var tradeId = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charA, TestSeasonId);
        var itemA = await inventoryA.AddItemAsync("QuickTradeItem", 1);

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);
        await tradeGrain.AddItemAsync(charA, itemA.Id);
        await tradeGrain.AcceptAsync(charA);
        await tradeGrain.AcceptAsync(charB);

        // Verify it's completed
        var session = await tradeGrain.GetSessionAsync();
        Assert.Equal(TradeStatus.Completed, session.Status);

        // Act - Wait past expiration time
        await Task.Delay(TimeSpan.FromSeconds(7));

        // Assert - Should still be completed, not expired
        var finalSession = await tradeGrain.GetSessionAsync();
        Assert.Equal(TradeStatus.Completed, finalSession.Status);
    }

    [Fact]
    public async Task ExpiredTrade_ShouldNotBeModifiable()
    {
        // Arrange - Create and expire a trade
        var charA = await CreateTestCharacterAsync();
        var charB = await CreateTestCharacterAsync();
        var tradeId = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(charA, TestSeasonId);
        var itemA = await inventoryA.AddItemAsync("ExpiredTradeItem", 1);

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);

        // Wait for expiration
        await Task.Delay(TimeSpan.FromSeconds(7));

        // Verify expired
        var session = await tradeGrain.GetSessionAsync();
        Assert.Equal(TradeStatus.Expired, session.Status);

        // Act & Assert - Should throw when trying to modify
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tradeGrain.AddItemAsync(charA, itemA.Id));
    }

    [Fact]
    public async Task ExpiredTrade_AcceptShouldReturnExpiredStatus()
    {
        // Arrange - Create and expire a trade
        var charA = await CreateTestCharacterAsync();
        var charB = await CreateTestCharacterAsync();
        var tradeId = Guid.NewGuid();

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);

        // Wait for expiration
        await Task.Delay(TimeSpan.FromSeconds(7));

        // Act - Try to accept
        var status = await tradeGrain.AcceptAsync(charA);

        // Assert - Should return Expired status
        Assert.Equal(TradeStatus.Expired, status);
    }

    [Fact]
    public async Task CancelledTrade_ShouldNotExpire()
    {
        // Arrange - Cancel a trade
        var charA = await CreateTestCharacterAsync();
        var charB = await CreateTestCharacterAsync();
        var tradeId = Guid.NewGuid();

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);
        await tradeGrain.CancelAsync(charA);

        // Verify it's cancelled
        var session = await tradeGrain.GetSessionAsync();
        Assert.Equal(TradeStatus.Cancelled, session.Status);

        // Act - Wait past expiration time
        await Task.Delay(TimeSpan.FromSeconds(7));

        // Assert - Should still be cancelled, not expired
        var finalSession = await tradeGrain.GetSessionAsync();
        Assert.Equal(TradeStatus.Cancelled, finalSession.Status);
    }
}
