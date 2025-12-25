using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Titan.Abstractions.Contracts;
using Titan.Abstractions.Models;
using Titan.Client.Encryption;
using TypedSignalR.Client;

namespace Titan.Client;

/// <summary>
/// Strongly-typed Titan API client.
/// Provides compile-time safe access to all hub operations.
/// </summary>
public sealed class TitanClient : IAsyncDisposable
{
    private readonly TitanClientOptions _options;
    private readonly SocketsHttpHandler _httpHandler;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TitanClient>? _logger;

    // Hub connections stored by path for thread-safe access
    private readonly ConcurrentDictionary<string, HubConnection> _hubs = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hubLocks = new();

    private readonly Lock _sessionLock = new();
    private string? _currentSessionId;
    private Guid? _currentUserId;
    private DateTimeOffset? _sessionExpiresAt;

    // Encryption support
    private IClientEncryptor? _encryptor;
    private bool _encryptionEnabled;
    private readonly SemaphoreSlim _encryptionInitLock = new(1, 1);

    // Disposal state
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Lock _disposalLock = new();
    private bool _disposed;
    private int _activeHubOperations;

    private bool IsDisposed
    {
        get { lock (_disposalLock) { return _disposed; } }
    }

    /// <summary>
    /// HTTP authentication client.
    /// </summary>
    public IAuthClient Auth { get; }

    /// <summary>
    /// Current user ID after successful login.
    /// </summary>
    public Guid? UserId => _currentUserId;

    /// <summary>
    /// Current session ID after successful login.
    /// </summary>
    public string? SessionId => _currentSessionId;

    /// <summary>
    /// Session expiration time.
    /// </summary>
    public DateTimeOffset? SessionExpiresAt => _sessionExpiresAt;

    /// <summary>
    /// Whether the client is authenticated.
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_currentSessionId);

    /// <summary>
    /// Whether payload encryption is active for this client.
    /// </summary>
    public bool IsEncryptionEnabled => _encryptionEnabled && _encryptor?.IsInitialized == true;

    /// <summary>
    /// Gets the client encryptor for advanced usage (testing, etc.).
    /// </summary>
    public IClientEncryptor? Encryptor => _encryptor;

    /// <summary>
    /// Creates a new TitanClient with the specified options.
    /// Use TitanClientBuilder for a fluent configuration API.
    /// </summary>
    public TitanClient(TitanClientOptions options)
    {
        _options = options;
        
        // Use SocketsHttpHandler with PooledConnectionLifetime to handle DNS changes
        // while avoiding socket exhaustion (Microsoft recommended pattern for SDK clients)
        _httpHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _httpClient = new HttpClient(_httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri(options.BaseUrl)
        };
        
        _logger = options.LoggerFactory?.CreateLogger<TitanClient>();
        Auth = new AuthClient(_httpClient, this);
    }

    /// <summary>
    /// Sets the session state after login.
    /// Called internally by AuthClient.
    /// </summary>
    internal void SetSession(string sessionId, Guid userId, DateTimeOffset expiresAt)
    {
        using (_sessionLock.EnterScope())
        {
            _currentSessionId = sessionId;
            _currentUserId = userId;
            _sessionExpiresAt = expiresAt;
            
            // Set Authorization header for subsequent HTTP requests
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionId);
        }
        
        _logger?.LogDebug("Session created for user {UserId}, expires {ExpiresAt}", userId, expiresAt);
    }

    /// <summary>
    /// Clears the session state after logout.
    /// Called internally by AuthClient.
    /// </summary>
    internal void ClearSession()
    {
        using (_sessionLock.EnterScope())
        {
            _currentSessionId = null;
            _currentUserId = null;
            _sessionExpiresAt = null;
            
            // Clear Authorization header
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
        
        _logger?.LogDebug("Session cleared");
    }

    /// <summary>
    /// Gets a typed Account hub client.
    /// </summary>
    public async Task<IAccountHubClient> GetAccountClientAsync()
    {
        var hub = await GetOrCreateHubAsync("/hub/account");
        return hub.CreateHubProxy<IAccountHubClient>();
    }

    /// <summary>
    /// Gets a typed Character hub client.
    /// </summary>
    public async Task<ICharacterHubClient> GetCharacterClientAsync()
    {
        var hub = await GetOrCreateHubAsync("/hub/character");
        return hub.CreateHubProxy<ICharacterHubClient>();
    }

    /// <summary>
    /// Gets a typed Inventory hub client.
    /// </summary>
    public async Task<IInventoryHubClient> GetInventoryClientAsync()
    {
        var hub = await GetOrCreateHubAsync("/hub/inventory");
        return hub.CreateHubProxy<IInventoryHubClient>();
    }

    /// <summary>
    /// Gets a typed Trade hub client and optionally registers for callbacks.
    /// </summary>
    /// <param name="receiver">Optional receiver for trade update callbacks.</param>
    public async Task<ITradeHubClient> GetTradeClientAsync(ITradeHubReceiver? receiver = null)
    {
        var hub = await GetOrCreateHubAsync("/hub/trade");

        if (receiver != null)
        {
            hub.Register(receiver);
        }

        return hub.CreateHubProxy<ITradeHubClient>();
    }

    /// <summary>
    /// Gets a typed BaseType hub client.
    /// </summary>
    public async Task<IBaseTypeHubClient> GetBaseTypeClientAsync()
    {
        var hub = await GetOrCreateHubAsync("/hub/base-type");
        return hub.CreateHubProxy<IBaseTypeHubClient>();
    }

    /// <summary>
    /// Gets a typed Season hub client.
    /// </summary>
    public async Task<ISeasonHubClient> GetSeasonClientAsync()
    {
        var hub = await GetOrCreateHubAsync("/hub/season");
        return hub.CreateHubProxy<ISeasonHubClient>();
    }

    /// <summary>
    /// Gets a typed Auth hub client for WebSocket-based auth operations.
    /// </summary>
    public async Task<IAuthHubClient> GetAuthHubClientAsync()
    {
        var hub = await GetOrCreateHubAsync("/hub/auth");
        return hub.CreateHubProxy<IAuthHubClient>();
    }

    /// <summary>
    /// Gets the raw HubConnection for a specific hub path.
    /// Useful for advanced scenarios like registering custom callbacks.
    /// </summary>
    public async Task<HubConnection> GetHubConnectionAsync(string hubPath)
    {
        return await GetOrCreateHubAsync(hubPath);
    }

    /// <summary>
    /// Thread-safe helper to get or create a hub connection.
    /// Uses per-hub locks to allow concurrent initialization of different hubs.
    /// </summary>
    private async Task<HubConnection> GetOrCreateHubAsync(string hubPath)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // Fast path: hub already exists
        if (_hubs.TryGetValue(hubPath, out var existing))
            return existing;

        // Track active operation to prevent disposal during hub creation
        Interlocked.Increment(ref _activeHubOperations);
        try
        {
            // Get or create a lock for this specific hub path
            var hubLock = _hubLocks.GetOrAdd(hubPath, _ => new SemaphoreSlim(1, 1));

            // Wait with disposal cancellation token
            await hubLock.WaitAsync(_disposalCts.Token);
            try
            {
                // Check disposal again after acquiring lock
                ObjectDisposedException.ThrowIf(IsDisposed, this);

                // Double-check: another thread may have created it while we waited
                if (_hubs.TryGetValue(hubPath, out existing))
                    return existing;

                var hub = await CreateAndConnectHubAsync(hubPath);
                _hubs[hubPath] = hub;
                return hub;
            }
            finally
            {
                hubLock.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeHubOperations);
        }
    }

    private async Task<HubConnection> CreateAndConnectHubAsync(string hubPath)
    {
        if (string.IsNullOrEmpty(_currentSessionId))
            throw new InvalidOperationException("Client is not authenticated. Call Auth.LoginAsync first.");

        var builder = new HubConnectionBuilder()
            .WithUrl($"{_options.BaseUrl}{hubPath}", options =>
            {
                // Session ticket is passed via access_token query parameter for SignalR
                options.AccessTokenProvider = () => Task.FromResult<string?>(_currentSessionId);
            });

        if (_options.EnableAutoReconnect)
        {
            builder.WithAutomaticReconnect();
        }

        var connection = builder.Build();

        connection.Closed += ex =>
        {
            _logger?.LogDebug("Disconnected from {HubPath}: {Error}", hubPath, ex?.Message);
            return Task.CompletedTask;
        };

        connection.Reconnecting += ex =>
        {
            _logger?.LogDebug("Reconnecting to {HubPath}: {Error}", hubPath, ex?.Message);
            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            _logger?.LogDebug("Reconnected to {HubPath}", hubPath);
            return Task.CompletedTask;
        };

        // Register key rotation handler for encryption hub
        if (hubPath == "/hub/encryption")
        {
            connection.On<KeyRotationRequest>("KeyRotation", async request =>
            {
                // Skip if client is being disposed
                if (IsDisposed || _disposalCts.IsCancellationRequested)
                {
                    _logger?.LogDebug("Skipping key rotation - client is disposing");
                    return;
                }

                try
                {
                    if (_encryptor != null)
                    {
                        var ack = _encryptor.HandleRotationRequest(request);
                        await connection.InvokeAsync("CompleteKeyRotation", ack, _disposalCts.Token);
                        _logger?.LogDebug("Completed key rotation, new KeyId: {KeyId}", request.KeyId);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                    _logger?.LogDebug("Key rotation cancelled - client is disposing");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Key rotation failed for KeyId: {KeyId}", request.KeyId);
                }
            });
        }

        await connection.StartAsync();
        _logger?.LogDebug("Connected to {HubPath}", hubPath);

        // Perform key exchange if encryption is enabled (skip for encryption hub itself to avoid recursion)
        if (_options.EnablePayloadEncryption && !_encryptionEnabled && hubPath != "/hub/encryption")
        {
            await _encryptionInitLock.WaitAsync();
            try
            {
                if (!_encryptionEnabled) // Double-check after acquiring lock
                {
                    await InitializeEncryptionAsync();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to initialize encryption for {HubPath}", hubPath);
            }
            finally
            {
                _encryptionInitLock.Release();
            }
        }

        return connection;
    }

    private async Task InitializeEncryptionAsync()
    {
        // Use GetOrCreateHubAsync to avoid resource leaks - encryption hub uses same pattern as other hubs
        var encryptionHub = await GetOrCreateHubAsync("/hub/encryption");
        _logger?.LogDebug("Connected to encryptionHub");

        // Perform key exchange
        _encryptor = new ClientEncryptor();
        
        var success = await _encryptor.PerformKeyExchangeAsync(async request =>
        {
            // Send key exchange request to server and get response
            return await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", request);
        });

        if (success)
        {
            _encryptionEnabled = true;
            _logger?.LogInformation("Encryption initialized, KeyId: {KeyId}", _encryptor.CurrentKeyId);
        }
    }

    /// <summary>
    /// Disconnects from all hubs and disposes resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (IsDisposed)
            return;

        // Mark as disposed first to prevent new operations
        lock (_disposalLock)
        {
            _disposed = true;
        }

        // Signal cancellation to unblock any waiters
        _disposalCts.Cancel();

        // Wait for active hub operations to complete (with timeout)
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        while (_activeHubOperations > 0)
        {
            var delay = Task.Delay(10);
            if (await Task.WhenAny(delay, timeout) == timeout)
            {
                _logger?.LogWarning("Timed out waiting for {Count} active hub operations to complete", _activeHubOperations);
                break;
            }
        }

        // Dispose encryptor (cleans up cryptographic key material securely)
        (_encryptor as IDisposable)?.Dispose();
        
        // Dispose all hub connections
        foreach (var hub in _hubs.Values)
        {
            try
            {
                await hub.StopAsync();
                await hub.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing hub connection");
            }
        }
        _hubs.Clear();

        // Dispose the per-hub initialization locks (safe now - no waiters)
        foreach (var hubLock in _hubLocks.Values)
        {
            hubLock.Dispose();
        }
        _hubLocks.Clear();

        // Dispose the encryption initialization lock
        _encryptionInitLock.Dispose();

        // Dispose the cancellation token source
        _disposalCts.Dispose();
        
        _httpClient.Dispose();
        _httpHandler.Dispose();
    }
}
