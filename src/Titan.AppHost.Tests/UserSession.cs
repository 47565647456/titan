using Microsoft.AspNetCore.SignalR.Client;

namespace Titan.AppHost.Tests;

/// <summary>
/// Manages a single set of hub connections for a user, reusing connections across operations.
/// This reduces connection overhead compared to creating new connections per operation.
/// </summary>
public class UserSession : IAsyncDisposable
{
    private readonly string _apiBaseUrl;
    private readonly Dictionary<string, HubConnection> _connections = new();
    
    public string SessionId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public Guid UserId { get; }

    public UserSession(string apiBaseUrl, string sessionId, DateTimeOffset expiresAt, Guid userId)
    {
        _apiBaseUrl = apiBaseUrl;
        SessionId = sessionId;
        ExpiresAt = expiresAt;
        UserId = userId;
    }

    /// <summary>
    /// Gets or creates a connected hub connection for the specified hub path.
    /// Connections are lazily created and cached for reuse.
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

        var connection = new HubConnectionBuilder()
            .WithUrl($"{_apiBaseUrl}{hubPath}?access_token={SessionId}")
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
    public Task<HubConnection> GetBroadcastHubAsync() => GetOrCreateHubAsync("/broadcastHub");

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
}
