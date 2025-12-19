using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for ticket-based WebSocket authentication.
/// Tests that connection tickets can be used instead of JWT tokens for SignalR connections.
/// </summary>
[Collection("AppHost")]
public class TicketAuthenticationTests : IntegrationTestBase
{
    public TicketAuthenticationTests(AppHostFixture fixture) : base(fixture)
    {
    }

    #region Connection Ticket Endpoint Tests

    [Fact]
    public async Task GetConnectionTicket_Authenticated_ReturnsTicket()
    {
        // Arrange
        var (accessToken, _, _, _) = await LoginAsUserAsync();

        // Act - use base class helper to get ticket
        var ticket = await GetConnectionTicketAsync(accessToken);

        // Assert
        Assert.NotNull(ticket);
        Assert.False(string.IsNullOrEmpty(ticket));
        // Ticket should be a 32-char hex string (GUID without dashes)
        Assert.Equal(32, ticket.Length);
    }

    [Fact]
    public async Task GetConnectionTicket_Unauthenticated_Returns401()
    {
        // Arrange - no auth header
        using var client = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };

        // Act
        var response = await client.PostAsync("/api/auth/connection-ticket", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Hub Connection with Ticket Tests

    [Fact]
    public async Task Hub_ConnectWithTicket_Succeeds()
    {
        // Arrange - Get a ticket
        var (accessToken, _, _, _) = await LoginAsUserAsync();
        var ticket = await GetConnectionTicketAsync(accessToken);

        // Act - Connect to hub with ticket (URL encode for safety)
        var hub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub?ticket={Uri.EscapeDataString(ticket)}")
            .Build();

        await hub.StartAsync();

        // Assert
        Assert.Equal(HubConnectionState.Connected, hub.State);

        // Cleanup
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task Hub_ConnectWithTicket_CanInvokeAuthenticatedMethods()
    {
        // Arrange - Get a ticket
        var (accessToken, _, _, _) = await LoginAsUserAsync();
        var ticket = await GetConnectionTicketAsync(accessToken);

        // Act - Connect to hub with ticket and invoke method
        var hub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub?ticket={Uri.EscapeDataString(ticket)}")
            .Build();

        await hub.StartAsync();

        // GetAccount requires authentication - should work via ticket
        var account = await hub.InvokeAsync<object>("GetAccount");

        // Assert
        Assert.NotNull(account);

        // Cleanup
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task Hub_ConnectWithInvalidTicket_Fails()
    {
        // Act - Connect to hub with invalid ticket
        var hub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub?ticket=invalid-ticket-12345")
            .Build();

        // Assert - Should throw or fail to authenticate
        await Assert.ThrowsAnyAsync<Exception>(() => hub.StartAsync());
    }

    #endregion

    #region Single-Use Ticket Tests

    [Fact]
    public async Task Ticket_WorksForMultipleRapidCalls_DuringHandshake()
    {
        // Arrange - Get a ticket
        var (accessToken, _, _, _) = await LoginAsUserAsync();
        var ticket = await GetConnectionTicketAsync(accessToken);

        // Act - Connect to hub (this will use the ticket for negotiate + websocket)
        var hub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub?ticket={Uri.EscapeDataString(ticket)}")
            .Build();
        
        await hub.StartAsync();
        Assert.Equal(HubConnectionState.Connected, hub.State);
        
        // Verify we can invoke methods (proves auth worked)
        var account = await hub.InvokeAsync<object>("GetAccount");
        Assert.NotNull(account);

        // Cleanup
        await hub.DisposeAsync();
    }

    #endregion

    #region Admin Ticket Tests

    [Fact]
    public async Task AdminTicket_PreservesAdminRole()
    {
        // Arrange - Login as admin and get ticket
        var (accessToken, _, _, _) = await LoginAsAdminAsync();
        var ticket = await GetConnectionTicketAsync(accessToken);

        // Act - Connect to admin hub with ticket
        var hub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/hubs/admin-metrics?ticket={Uri.EscapeDataString(ticket)}")
            .Build();

        await hub.StartAsync();

        // Assert - Admin hub requires admin role, connection should work
        Assert.Equal(HubConnectionState.Connected, hub.State);

        // Cleanup
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task NonAdminTicket_CannotAccessAdminHub()
    {
        // Arrange - Login as normal user and get ticket
        var (accessToken, _, _, _) = await LoginAsUserAsync();
        var ticket = await GetConnectionTicketAsync(accessToken);

        // Act - Try to connect to admin hub with non-admin ticket
        var hub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/hubs/admin-metrics?ticket={Uri.EscapeDataString(ticket)}")
            .Build();

        // Assert - Should fail due to lack of admin role
        await Assert.ThrowsAnyAsync<Exception>(() => hub.StartAsync());
    }

    #endregion

    private record ConnectionTicketResponse(string Ticket);
}
