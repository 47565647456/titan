using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Titan.AppHost.Tests;

/// <summary>
/// Manages a single set of hub connections for a user, reusing connections across operations.
/// Uses ticket-based authentication to prevent JWT tokens from appearing in server logs.
/// </summary>
public class UserSession : IAsyncDisposable
{
    private readonly string _apiBaseUrl;
    private readonly Dictionary<string, HubConnection> _connections = new();
    
    public string Token { get; }
    public string RefreshToken { get; }
    public int AccessTokenExpiresInSeconds { get; }
    public Guid UserId { get; }

    public UserSession(string apiBaseUrl, string token, string refreshToken, int expiresInSeconds, Guid userId)
    {
        _apiBaseUrl = apiBaseUrl;
        Token = token;
        RefreshToken = refreshToken;
        AccessTokenExpiresInSeconds = expiresInSeconds;
        UserId = userId;
    }

    /// <summary>
    /// Fetches a short-lived connection ticket for WebSocket authentication.
    /// </summary>
    private async Task<string> GetConnectionTicketAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        var response = await client.PostAsync("/api/auth/connection-ticket", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ConnectionTicketResponse>();
        return result?.Ticket ?? throw new InvalidOperationException("Failed to get connection ticket");
    }

    /// <summary>
    /// Gets or creates a connected hub connection for the specified hub path.
    /// Connections are lazily created and cached for reuse.
    /// Uses ticket-based authentication to prevent JWT exposure in logs.
    /// </summary>
    private async Task<HubConnection> GetOrCreateHubAsync(string hubPath)
    {
        if (_connections.TryGetValue(hubPath, out var existing))
        {
            if (existing.State == HubConnectionState.Connected)
                return existing;
            
            // Connection dropped, recreate it
            await existing.DisposeAsync();
            _connections.Remove(hubPath);
        }

        // Fetch a new ticket for this connection
        var ticket = await GetConnectionTicketAsync();
        
        var connection = new HubConnectionBuilder()
            .WithUrl($"{_apiBaseUrl}{hubPath}?ticket={Uri.EscapeDataString(ticket)}")
            .Build();
        
        await connection.StartAsync();
        _connections[hubPath] = connection;
        return connection;
    }

    // Hub accessors - lazily connected on first access
    public Task<HubConnection> GetAccountHubAsync() => GetOrCreateHubAsync("/accountHub");
    public Task<HubConnection> GetCharacterHubAsync() => GetOrCreateHubAsync("/characterHub");
    public Task<HubConnection> GetInventoryHubAsync() => GetOrCreateHubAsync("/inventoryHub");
    public Task<HubConnection> GetTradeHubAsync() => GetOrCreateHubAsync("/tradeHub");
    public Task<HubConnection> GetBaseTypeHubAsync() => GetOrCreateHubAsync("/baseTypeHub");
    public Task<HubConnection> GetSeasonHubAsync() => GetOrCreateHubAsync("/seasonHub");

    /// <summary>
    /// Gets the number of active hub connections.
    /// </summary>
    public int ConnectionCount => _connections.Count(c => c.Value.State == HubConnectionState.Connected);

    /// <summary>
    /// Disconnects from a specific hub.
    /// </summary>
    public async Task DisconnectHubAsync(string hubPath)
    {
        if (_connections.TryGetValue(hubPath, out var connection))
        {
            _connections.Remove(hubPath);
            try
            {
                await connection.StopAsync();
                await connection.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
        _connections.Clear();
    }

    private record ConnectionTicketResponse(string Ticket);
}
