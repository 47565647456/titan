using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Unit tests for SessionLogGrain (persisted session logging).
/// </summary>
public class SessionLogGrainTests : IAsyncLifetime
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
    public async Task StartSession_CreatesNewSession()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<ISessionLogGrain>(userId);

        // Act
        var sessionId = await grain.StartSessionAsync("192.168.1.1");
        var sessions = await grain.GetRecentSessionsAsync();

        // Assert
        Assert.NotEqual(Guid.Empty, sessionId);
        Assert.Single(sessions);
        Assert.Equal(sessionId, sessions[0].SessionId);
        Assert.Equal(userId, sessions[0].UserId);
        Assert.Equal("192.168.1.1", sessions[0].IpAddress);
        Assert.Null(sessions[0].LogoutAt);
    }

    [Fact]
    public async Task EndSession_RecordsLogoutTime()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<ISessionLogGrain>(userId);
        var sessionId = await grain.StartSessionAsync("10.0.0.1");

        // Act
        await Task.Delay(100); // Brief delay to ensure time passes
        await grain.EndSessionAsync();
        var sessions = await grain.GetRecentSessionsAsync();

        // Assert
        Assert.Single(sessions);
        Assert.NotNull(sessions[0].LogoutAt);
        Assert.NotNull(sessions[0].Duration);
        Assert.True(sessions[0].Duration!.Value.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task MultipleSessions_RecordsHistory()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<ISessionLogGrain>(userId);

        // Act - Create 3 sessions
        await grain.StartSessionAsync("1.1.1.1");
        await grain.EndSessionAsync();
        
        await grain.StartSessionAsync("2.2.2.2");
        await grain.EndSessionAsync();
        
        await grain.StartSessionAsync("3.3.3.3");
        await grain.EndSessionAsync();

        var sessions = await grain.GetRecentSessionsAsync();

        // Assert
        Assert.Equal(3, sessions.Count);
        // Most recent first
        Assert.Equal("3.3.3.3", sessions[0].IpAddress);
        Assert.Equal("2.2.2.2", sessions[1].IpAddress);
        Assert.Equal("1.1.1.1", sessions[2].IpAddress);
    }

    [Fact]
    public async Task GetRecentSessions_RespectsCountLimit()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<ISessionLogGrain>(userId);

        // Create 5 sessions
        for (int i = 0; i < 5; i++)
        {
            await grain.StartSessionAsync($"1.1.1.{i}");
            await grain.EndSessionAsync();
        }

        // Act
        var sessions = await grain.GetRecentSessionsAsync(3);

        // Assert
        Assert.Equal(3, sessions.Count);
    }

    [Fact]
    public async Task StartSession_WithNullIp_Works()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<ISessionLogGrain>(userId);

        // Act
        var sessionId = await grain.StartSessionAsync(null);
        var sessions = await grain.GetRecentSessionsAsync();

        // Assert
        Assert.NotEqual(Guid.Empty, sessionId);
        Assert.Single(sessions);
        Assert.Null(sessions[0].IpAddress);
    }

    [Fact]
    public async Task SessionHistory_PrunesOldEntries_WhenExceedingMaxSize()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<ISessionLogGrain>(userId);
        const int maxHistorySize = 100; // Matches SessionLogGrain.MaxHistorySize

        // Create 105 sessions (5 more than max)
        for (int i = 0; i < maxHistorySize + 5; i++)
        {
            await grain.StartSessionAsync($"10.0.0.{i % 256}");
            await grain.EndSessionAsync();
        }

        // Act - get all sessions (up to a large count)
        var sessions = await grain.GetRecentSessionsAsync(200);

        // Assert - should be capped at MaxHistorySize
        Assert.Equal(maxHistorySize, sessions.Count);
        
        // The oldest sessions should have been pruned (IPs 10.0.0.0 through 10.0.0.4)
        // The newest session should be 10.0.0.104 % 256 = 10.0.0.104
        Assert.DoesNotContain(sessions, s => s.IpAddress == "10.0.0.0");
        Assert.DoesNotContain(sessions, s => s.IpAddress == "10.0.0.4");
    }

    [Fact]
    public async Task EndSession_WhenNoActiveSession_DoesNotThrow()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<ISessionLogGrain>(userId);

        // Act - call EndSession without starting one (should be idempotent)
        await grain.EndSessionAsync();
        var sessions = await grain.GetRecentSessionsAsync();

        // Assert - no crash, no sessions
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task EndSession_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<ISessionLogGrain>(userId);
        await grain.StartSessionAsync("1.2.3.4");
        await grain.EndSessionAsync();

        // Act - call EndSession again (should be idempotent)
        await grain.EndSessionAsync();
        var sessions = await grain.GetRecentSessionsAsync();

        // Assert - still just one session, no crash
        Assert.Single(sessions);
        Assert.NotNull(sessions[0].LogoutAt);
    }
}

