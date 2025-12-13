using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for connection tracking features.
/// Tests that player presence and session logging work correctly
/// when clients connect/disconnect via SignalR hubs through the API gateway.
/// </summary>
[Collection("AppHost")]
public class ConnectionTrackingTests : IntegrationTestBase
{
    public ConnectionTrackingTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ConnectToHub_TracksPresence()
    {
        // Arrange
        var session = await CreateUserSessionAsync();

        // Act - Connect to AccountHub
        var hub = await session.GetAccountHubAsync();
        
        // Verify we can make a call (connection is established and tracked)
        var account = await hub.InvokeAsync<Account>("GetAccount");

        // Assert
        Assert.NotNull(account);
        Assert.Equal(session.UserId, account.AccountId);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task MultipleHubConnections_AllTracked()
    {
        // Arrange
        var session = await CreateUserSessionAsync();

        // Act - Connect to multiple hubs
        var accountHub = await session.GetAccountHubAsync();
        var characterHub = await session.GetCharacterHubAsync();
        var inventoryHub = await session.GetInventoryHubAsync();

        // Verify all connections work
        var account = await accountHub.InvokeAsync<Account>("GetAccount");
        var characters = await accountHub.InvokeAsync<IReadOnlyList<CharacterSummary>>("GetCharacters");

        // Assert - all connections functional
        Assert.NotNull(account);
        Assert.NotNull(characters);
        Assert.Equal(3, session.ConnectionCount);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task DisconnectFromHub_UpdatesPresence()
    {
        // Arrange
        var session = await CreateUserSessionAsync();
        
        // Connect to a hub
        _ = await session.GetAccountHubAsync();
        Assert.Equal(1, session.ConnectionCount);

        // Act - Disconnect
        await session.DisconnectHubAsync("/accountHub");

        // Assert
        Assert.Equal(0, session.ConnectionCount);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task SessionDispose_DisconnectsAllHubs()
    {
        // Arrange
        var session = await CreateUserSessionAsync();
        var accountHub = await session.GetAccountHubAsync();
        var characterHub = await session.GetCharacterHubAsync();
        Assert.Equal(2, session.ConnectionCount);
        Assert.Equal(HubConnectionState.Connected, accountHub.State);
        Assert.Equal(HubConnectionState.Connected, characterHub.State);

        // Act
        await session.DisposeAsync();

        // Assert - both hub connections should be disconnected
        Assert.Equal(HubConnectionState.Disconnected, accountHub.State);
        Assert.Equal(HubConnectionState.Disconnected, characterHub.State);
    }

    [Fact]
    public async Task ReconnectToHub_AfterDisconnect_Works()
    {
        // Arrange
        var session = await CreateUserSessionAsync();
        
        // Connect, use, disconnect
        var hub1 = await session.GetAccountHubAsync();
        await hub1.InvokeAsync<Account>("GetAccount");
        await session.DisconnectHubAsync("/accountHub");
        Assert.Equal(0, session.ConnectionCount);

        // Act - Reconnect
        var hub2 = await session.GetAccountHubAsync();
        var account = await hub2.InvokeAsync<Account>("GetAccount");

        // Assert
        Assert.NotNull(account);
        Assert.Equal(1, session.ConnectionCount);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task TwoUsers_IndependentSessions()
    {
        // Arrange
        var session1 = await CreateUserSessionAsync();
        var session2 = await CreateUserSessionAsync();

        // Act - Both connect
        var hub1 = await session1.GetAccountHubAsync();
        var hub2 = await session2.GetAccountHubAsync();

        var account1 = await hub1.InvokeAsync<Account>("GetAccount");
        var account2 = await hub2.InvokeAsync<Account>("GetAccount");

        // Assert - Independent users
        Assert.NotEqual(session1.UserId, session2.UserId);
        Assert.NotEqual(account1.AccountId, account2.AccountId);
        Assert.Equal(1, session1.ConnectionCount);
        Assert.Equal(1, session2.ConnectionCount);

        await session1.DisposeAsync();
        await session2.DisposeAsync();
    }
}
