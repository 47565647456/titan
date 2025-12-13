using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Titan.Client;

/// <summary>
/// Manages a set of SignalR hub connections for a user session.
/// Connections are lazily created and cached for reuse across operations.
/// Supports automatic token refresh over existing WebSocket connections.
/// Thread-safe for concurrent access.
/// </summary>
public class TitanSession : IAsyncDisposable
{
    private readonly string _apiBaseUrl;
    private readonly Dictionary<string, HubConnection> _connections = new();
    private readonly ILogger<TitanSession>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    
    private string _accessToken;
    private string _refreshToken;
    private int _accessTokenExpiresInSeconds;
    private Timer? _refreshTimer;
    private bool _autoRefreshEnabled;
    private bool _disposed;

    /// <summary>
    /// Buffer time before token expiry to trigger refresh (30 seconds).
    /// </summary>
    private const int RefreshBufferSeconds = 30;

    /// <summary>
    /// The current JWT access token used for authentication.
    /// </summary>
    public string AccessToken
    {
        get
        {
            _semaphore.Wait();
            try { return _accessToken; }
            finally { _semaphore.Release(); }
        }
    }

    /// <summary>
    /// The current refresh token for obtaining new access tokens.
    /// </summary>
    public string RefreshToken
    {
        get
        {
            _semaphore.Wait();
            try { return _refreshToken; }
            finally { _semaphore.Release(); }
        }
    }

    /// <summary>
    /// Alias for AccessToken for backwards compatibility.
    /// </summary>
    public string Token => AccessToken;

    /// <summary>
    /// The authenticated user's ID.
    /// </summary>
    public Guid UserId { get; }

    /// <summary>
    /// Whether automatic token refresh is enabled.
    /// </summary>
    public bool AutoRefreshEnabled => _autoRefreshEnabled;

    #region Events

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

    /// <summary>
    /// Fired when the access token has been refreshed successfully.
    /// </summary>
    public event Action<string>? OnTokenRefreshed;

    /// <summary>
    /// Fired when token refresh fails (e.g., refresh token expired).
    /// Client should prompt re-authentication.
    /// </summary>
    public event Action<Exception>? OnTokenRefreshFailed;

    #endregion

    /// <summary>
    /// Creates a new session with access and refresh tokens.
    /// Automatically schedules token refresh before expiry.
    /// </summary>
    /// <param name="apiBaseUrl">The API base URL.</param>
    /// <param name="accessToken">The initial access token.</param>
    /// <param name="refreshToken">The refresh token for token renewal.</param>
    /// <param name="accessTokenExpiresInSeconds">Seconds until access token expires.</param>
    /// <param name="userId">The authenticated user's ID.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="enableAutoRefresh">Whether to enable automatic token refresh (default: true).</param>
    public TitanSession(
        string apiBaseUrl, 
        string accessToken, 
        string refreshToken,
        int accessTokenExpiresInSeconds,
        Guid userId, 
        ILogger<TitanSession>? logger = null,
        bool enableAutoRefresh = true)
    {
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _accessTokenExpiresInSeconds = accessTokenExpiresInSeconds;
        UserId = userId;
        _logger = logger;
        _autoRefreshEnabled = enableAutoRefresh;

        if (enableAutoRefresh)
        {
            ScheduleTokenRefresh();
        }
    }

    /// <summary>
    /// Legacy constructor for backwards compatibility.
    /// Auto-refresh is disabled when using this constructor.
    /// </summary>
    [Obsolete("Use the constructor with accessToken, refreshToken, and expiresInSeconds for auto-refresh support.")]
    public TitanSession(string apiBaseUrl, string token, Guid userId, ILogger<TitanSession>? logger = null)
        : this(apiBaseUrl, token, string.Empty, 0, userId, logger, enableAutoRefresh: false)
    {
    }

    #region Token Refresh

    /// <summary>
    /// Schedules automatic token refresh before expiry.
    /// </summary>
    private void ScheduleTokenRefresh()
    {
        if (_disposed || _accessTokenExpiresInSeconds <= 0)
            return;

        // Refresh 30 seconds before expiry, but at least 1 second from now
        var delaySeconds = Math.Max(_accessTokenExpiresInSeconds - RefreshBufferSeconds, 1);
        var delay = TimeSpan.FromSeconds(delaySeconds);

        _refreshTimer?.Dispose();
        _refreshTimer = new Timer(
            RefreshTokenCallback,
            null,
            delay,
            Timeout.InfiniteTimeSpan);

        _logger?.LogDebug(
            "Token refresh scheduled in {Seconds}s for user {UserId}", 
            delaySeconds, UserId);
    }

    /// <summary>
    /// Timer callback that triggers token refresh.
    /// </summary>
    private async void RefreshTokenCallback(object? state)
    {
        if (_disposed)
            return;

        try
        {
            await RefreshTokenAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Automatic token refresh failed for user {UserId}", UserId);
            OnTokenRefreshFailed?.Invoke(ex);
        }
    }

    /// <summary>
    /// Manually triggers a token refresh over the existing WebSocket connection.
    /// </summary>
    public async Task RefreshTokenAsync()
    {
        var authHub = await GetAuthHubAsync();
        
        string currentRefreshToken;
        await _semaphore.WaitAsync();
        try
        {
            currentRefreshToken = _refreshToken;
        }
        finally
        {
            _semaphore.Release();
        }

        // Call RefreshToken over existing WebSocket
        var result = await authHub.InvokeAsync<RefreshResult>("RefreshToken", currentRefreshToken, UserId);

        await _semaphore.WaitAsync();
        try
        {
            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _accessTokenExpiresInSeconds = result.AccessTokenExpiresInSeconds;
        }
        finally
        {
            _semaphore.Release();
        }

        _logger?.LogDebug("Token refreshed for user {UserId}", UserId);
        OnTokenRefreshed?.Invoke(result.AccessToken);

        // Schedule next refresh
        if (_autoRefreshEnabled)
        {
            ScheduleTokenRefresh();
        }
    }

    /// <summary>
    /// Stops automatic token refresh.
    /// </summary>
    public void StopAutoRefresh()
    {
        _autoRefreshEnabled = false;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _logger?.LogDebug("Auto-refresh stopped for user {UserId}", UserId);
    }

    /// <summary>
    /// Starts automatic token refresh if not already running.
    /// </summary>
    public void StartAutoRefresh()
    {
        if (!_autoRefreshEnabled)
        {
            _autoRefreshEnabled = true;
            ScheduleTokenRefresh();
            _logger?.LogDebug("Auto-refresh started for user {UserId}", UserId);
        }
    }

    /// <summary>
    /// Updates the tokens manually (e.g., after explicit refresh call).
    /// </summary>
    public async Task UpdateTokensAsync(string accessToken, string refreshToken, int expiresInSeconds)
    {
        await _semaphore.WaitAsync();
        try
        {
            _accessToken = accessToken;
            _refreshToken = refreshToken;
            _accessTokenExpiresInSeconds = expiresInSeconds;
            _logger?.LogDebug("Tokens updated for user {UserId}", UserId);
        }
        finally
        {
            _semaphore.Release();
        }

        if (_autoRefreshEnabled)
        {
            ScheduleTokenRefresh();
        }
    }

    /// <summary>
    /// Updates the JWT token for new connections.
    /// Note: Existing connections will continue using the old token until reconnected.
    /// </summary>
    [Obsolete("Use UpdateTokensAsync for full token update support.")]
    public async Task UpdateTokenAsync(string newToken)
    {
        await _semaphore.WaitAsync();
        try
        {
            _accessToken = newToken;
            _logger?.LogDebug("Token updated for user {UserId}", UserId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    #endregion

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
                .WithUrl($"{_apiBaseUrl}{hubPath}?access_token={_accessToken}")
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

    /// <summary>Gets the AuthHub connection (for profile and token refresh).</summary>
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
        if (_disposed)
            return;

        _disposed = true;
        _refreshTimer?.Dispose();
        
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

/// <summary>
/// Result of a token refresh operation.
/// </summary>
public record RefreshResult(
    string AccessToken,
    string RefreshToken,
    int AccessTokenExpiresInSeconds);
