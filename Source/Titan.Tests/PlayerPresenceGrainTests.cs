using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Unit tests for PlayerPresenceGrain (in-memory presence tracking).
/// </summary>
public class PlayerPresenceGrainTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IGrainFactory _grainFactory => _cluster.GrainFactory;

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
    public async Task RegisterConnection_SetsOnlineStatus()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<IPlayerPresenceGrain>(userId);

        // Act
        await grain.RegisterConnectionAsync("conn-1", "AccountHub");
        var presence = await grain.GetPresenceAsync();

        // Assert
        Assert.True(presence.IsOnline);
        Assert.Equal(1, presence.ConnectionCount);
        Assert.Equal(userId, presence.UserId);
    }

    [Fact]
    public async Task MultipleConnections_IncreasesCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<IPlayerPresenceGrain>(userId);

        // Act
        await grain.RegisterConnectionAsync("conn-1", "AccountHub");
        await grain.RegisterConnectionAsync("conn-2", "TradeHub");
        await grain.RegisterConnectionAsync("conn-3", "CharacterHub");

        // Assert
        Assert.Equal(3, await grain.GetConnectionCountAsync());
        Assert.True(await grain.IsOnlineAsync());
    }

    [Fact]
    public async Task UnregisterConnection_DecreasesCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<IPlayerPresenceGrain>(userId);
        await grain.RegisterConnectionAsync("conn-1", "AccountHub");
        await grain.RegisterConnectionAsync("conn-2", "TradeHub");

        // Act
        await grain.UnregisterConnectionAsync("conn-1");

        // Assert
        Assert.Equal(1, await grain.GetConnectionCountAsync());
        Assert.True(await grain.IsOnlineAsync());
    }

    [Fact]
    public async Task LastConnectionRemoved_GoesOffline()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<IPlayerPresenceGrain>(userId);
        await grain.RegisterConnectionAsync("conn-1", "AccountHub");

        // Act
        await grain.UnregisterConnectionAsync("conn-1");

        // Assert
        Assert.Equal(0, await grain.GetConnectionCountAsync());
        Assert.False(await grain.IsOnlineAsync());
    }

    [Fact]
    public async Task SetActivity_UpdatesCurrentActivity()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<IPlayerPresenceGrain>(userId);
        await grain.RegisterConnectionAsync("conn-1", "TradeHub");

        // Act
        await grain.SetActivityAsync("trading");
        var presence = await grain.GetPresenceAsync();

        // Assert
        Assert.Equal("trading", presence.CurrentActivity);
    }

    [Fact]
    public async Task GetPresence_TracksLastSeen()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<IPlayerPresenceGrain>(userId);
        var beforeConnect = DateTimeOffset.UtcNow;

        // Act
        await grain.RegisterConnectionAsync("conn-1", "AccountHub");
        var presence = await grain.GetPresenceAsync();

        // Assert
        Assert.True(presence.LastSeen >= beforeConnect);
    }
}
