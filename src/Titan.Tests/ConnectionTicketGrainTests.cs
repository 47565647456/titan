using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Unit tests for ConnectionTicketGrain.
/// Tests short-lived, single-use ticket authentication for WebSockets.
/// </summary>
[Collection(ClusterCollection.Name)]
public class ConnectionTicketGrainTests
{
    private readonly TestCluster _cluster;

    public ConnectionTicketGrainTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
    }

    [Fact]
    public async Task CreateTicketAsync_ReturnsValidTicket()
    {
        // Arrange
        var ticketId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var roles = new[] { "Admin", "User" };
        var grain = _cluster.GrainFactory.GetGrain<IConnectionTicketGrain>(ticketId);

        // Act
        var ticket = await grain.CreateTicketAsync(userId, roles, TimeSpan.FromSeconds(30));

        // Assert
        Assert.NotNull(ticket);
        Assert.Equal(ticketId, ticket.TicketId);
        Assert.Equal(userId, ticket.UserId);
        Assert.Equal(roles, ticket.Roles);
        Assert.False(ticket.IsConsumed);
        Assert.True(ticket.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ValidTicket_ReturnsTicket()
    {
        // Arrange
        var ticketId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var roles = new[] { "User" };
        var grain = _cluster.GrainFactory.GetGrain<IConnectionTicketGrain>(ticketId);
        await grain.CreateTicketAsync(userId, roles, TimeSpan.FromSeconds(30));

        // Act
        var consumedTicket = await grain.ValidateAndConsumeAsync();

        // Assert
        Assert.NotNull(consumedTicket);
        Assert.Equal(ticketId, consumedTicket.TicketId);
        Assert.Equal(userId, consumedTicket.UserId);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_MultipleCallsWithinHandshakeWindow_Succeed()
    {
        // Arrange
        var ticketId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var roles = new[] { "User" };
        var grain = _cluster.GrainFactory.GetGrain<IConnectionTicketGrain>(ticketId);
        await grain.CreateTicketAsync(userId, roles, TimeSpan.FromSeconds(30));

        // First validation - starts handshake window
        var firstResult = await grain.ValidateAndConsumeAsync();
        Assert.NotNull(firstResult);

        // Act - Second validation within handshake window (SignalR negotiate then websocket)
        var secondResult = await grain.ValidateAndConsumeAsync();

        // Assert - should succeed as within 10-second handshake window
        Assert.NotNull(secondResult);
        Assert.Equal(userId, secondResult.UserId);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_NoTicketCreated_ReturnsNull()
    {
        // Arrange - get grain without creating ticket
        var ticketId = Guid.NewGuid().ToString("N");
        var grain = _cluster.GrainFactory.GetGrain<IConnectionTicketGrain>(ticketId);

        // Act
        var result = await grain.ValidateAndConsumeAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_ExpiredTicket_ReturnsNull()
    {
        // Arrange - create ticket with very short lifetime
        var ticketId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var roles = new[] { "User" };
        var grain = _cluster.GrainFactory.GetGrain<IConnectionTicketGrain>(ticketId);
        await grain.CreateTicketAsync(userId, roles, TimeSpan.FromMilliseconds(1));

        // Wait for expiration
        await Task.Delay(50);

        // Act
        var result = await grain.ValidateAndConsumeAsync();

        // Assert - expired ticket returns null
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateTicketAsync_DefaultLifetime_Uses30Seconds()
    {
        // Arrange
        var ticketId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IConnectionTicketGrain>(ticketId);

        // Act
        var ticket = await grain.CreateTicketAsync(userId, Array.Empty<string>());

        // Assert - default lifetime should be ~30 seconds
        var expectedExpiry = DateTimeOffset.UtcNow.AddSeconds(30);
        var tolerance = TimeSpan.FromSeconds(2);
        Assert.True(Math.Abs((ticket.ExpiresAt - expectedExpiry).TotalSeconds) < tolerance.TotalSeconds);
    }

    [Fact]
    public async Task CreateTicketAsync_PreservesRoles()
    {
        // Arrange
        var ticketId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var roles = new[] { "Admin", "SuperAdmin", "User" };
        var grain = _cluster.GrainFactory.GetGrain<IConnectionTicketGrain>(ticketId);

        // Act
        var ticket = await grain.CreateTicketAsync(userId, roles);

        // Assert
        Assert.Equal(3, ticket.Roles.Length);
        Assert.Contains("Admin", ticket.Roles);
        Assert.Contains("SuperAdmin", ticket.Roles);
        Assert.Contains("User", ticket.Roles);
    }
}
