using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Titan.Client;

/// <summary>
/// Manages a set of SignalR hub connections for a user session.
/// Connections are lazily created and cached for reuse across operations.
/// Thread-safe for concurrent access.
/// </summary>
public class TitanSession : IAsyncDisposable
{
    private readonly string _apiBaseUrl;
    private readonly Dictionary<string, HubConnection> _connections = new();
    private readonly ILogger<TitanSession>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string _token;

    /// <summary>
    /// The current JWT token used for authentication.
    /// </summary>
    public string Token
    {
        get
        {
            _semaphore.Wait();
            try { return _token; }
            finally { _semaphore.Release(); }
        }
    }

    /// <summary>
    /// The authenticated user's ID.
    /// </summary>
    public Guid UserId { get; }

    /// <summary>
    /// Fired when a hub connection is established.
    /// </summary>
    public event Action<string>? OnConnected;

    /// <summary>
    /// Fired when a hub connection is closed.
    /// </summary>
    public event Action<string, Exception?>? OnDisconnected;

    /// <summary>
    /// Fired when a hub connection is attempting to reconnect.
    /// </summary>
    public event Action<string, Exception?>? OnReconnecting;

    /// <summary>
    /// Fired when a hub connection has reconnected.
    /// </summary>
    public event Action<string>? OnReconnected;

    public TitanSession(string apiBaseUrl, string token, Guid userId, ILogger<TitanSession>? logger = null)
    {
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _token = token;
        UserId = userId;
        _logger = logger;
    }

    /// <summary>
    /// Updates the JWT token for new connections.
    /// Note: Existing connections will continue using the old token until reconnected.
    /// </summary>
    public async Task UpdateTokenAsync(string newToken)
    {
        await _semaphore.WaitAsync();
        try
        {
            _token = newToken;
            _logger?.LogDebug("Token updated for user {UserId}", UserId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Gets or creates a connected hub connection for the specified hub path.
    /// Connections are lazily created and cached for reuse.
    /// Thread-safe for concurrent access.
    /// </summary>
    private async Task<HubConnection> GetOrCreateHubAsync(string hubPath)
    {
        await _semaphore.WaitAsync();
        try
        {
            // Check if we have an existing connected connection
            if (_connections.TryGetValue(hubPath, out var existing))
            {
                if (existing.State == HubConnectionState.Connected)
                    return existing;

                // Connection dropped, dispose it
                try
                {
                    await existing.DisposeAsync();
                }
                catch { /* Ignore disposal errors */ }
                _connections.Remove(hubPath);
            }

            // Create new connection (still holding semaphore to prevent duplicates)
            var connection = new HubConnectionBuilder()
                .WithUrl($"{_apiBaseUrl}{hubPath}?access_token={_token}")
                .WithAutomaticReconnect()
                .Build();

            // Wire up events
            connection.Closed += ex =>
            {
                OnDisconnected?.Invoke(hubPath, ex);
                _logger?.LogDebug("Disconnected from {HubPath}: {Error}", hubPath, ex?.Message);
                return Task.CompletedTask;
            };

            connection.Reconnecting += ex =>
            {
                OnReconnecting?.Invoke(hubPath, ex);
                _logger?.LogDebug("Reconnecting to {HubPath}: {Error}", hubPath, ex?.Message);
                return Task.CompletedTask;
            };

            connection.Reconnected += connectionId =>
            {
                OnReconnected?.Invoke(hubPath);
                _logger?.LogDebug("Reconnected to {HubPath}", hubPath);
                return Task.CompletedTask;
            };

            await connection.StartAsync();
            _connections[hubPath] = connection;

            OnConnected?.Invoke(hubPath);
            _logger?.LogDebug("Connected to {HubPath}", hubPath);

            return connection;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    #region Hub Accessors

    /// <summary>Gets the AccountHub connection.</summary>
    public Task<HubConnection> GetAccountHubAsync() => GetOrCreateHubAsync("/accountHub");

    /// <summary>Gets the AuthHub connection (for profile operations).</summary>
    public Task<HubConnection> GetAuthHubAsync() => GetOrCreateHubAsync("/authHub");

    /// <summary>Gets the CharacterHub connection.</summary>
    public Task<HubConnection> GetCharacterHubAsync() => GetOrCreateHubAsync("/characterHub");

    /// <summary>Gets the InventoryHub connection.</summary>
    public Task<HubConnection> GetInventoryHubAsync() => GetOrCreateHubAsync("/inventoryHub");

    /// <summary>Gets the TradeHub connection.</summary>
    public Task<HubConnection> GetTradeHubAsync() => GetOrCreateHubAsync("/tradeHub");

    /// <summary>Gets the ItemTypeHub connection.</summary>
    public Task<HubConnection> GetItemTypeHubAsync() => GetOrCreateHubAsync("/itemTypeHub");

    /// <summary>Gets the SeasonHub connection.</summary>
    public Task<HubConnection> GetSeasonHubAsync() => GetOrCreateHubAsync("/seasonHub");

    #endregion

    /// <summary>
    /// Disconnects from a specific hub.
    /// </summary>
    public async Task DisconnectHubAsync(string hubPath)
    {
        HubConnection? connection = null;
        
        await _semaphore.WaitAsync();
        try
        {
            if (_connections.TryGetValue(hubPath, out connection))
            {
                _connections.Remove(hubPath);
            }
        }
        finally
        {
            _semaphore.Release();
        }

        if (connection != null)
        {
            try
            {
                await connection.StopAsync();
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disconnecting from {HubPath}", hubPath);
            }
        }
    }

    /// <summary>
    /// Gets the number of active hub connections.
    /// </summary>
    public async Task<int> GetConnectionCountAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            return _connections.Count(c => c.Value.State == HubConnectionState.Connected);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Gets the number of active hub connections (synchronous, for test compatibility).
    /// </summary>
    public int ConnectionCount
    {
        get
        {
            _semaphore.Wait();
            try
            {
                return _connections.Count(c => c.Value.State == HubConnectionState.Connected);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<HubConnection> toDispose;
        
        await _semaphore.WaitAsync();
        try
        {
            toDispose = _connections.Values.ToList();
            _connections.Clear();
        }
        finally
        {
            _semaphore.Release();
        }

        foreach (var connection in toDispose)
        {
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
        
        _semaphore.Dispose();
    }
}
