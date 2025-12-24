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

    private HubConnection? _accountHub;
    private HubConnection? _characterHub;
    private HubConnection? _inventoryHub;
    private HubConnection? _tradeHub;
    private HubConnection? _baseTypeHub;
    private HubConnection? _seasonHub;
    private HubConnection? _authHub;
    private HubConnection? _encryptionHub;

    private readonly Lock _sessionLock = new();
    private readonly SemaphoreSlim _hubInitLock = new(1, 1);  // Thread-safe hub initialization
    private string? _currentSessionId;
    private Guid? _currentUserId;
    private DateTimeOffset? _sessionExpiresAt;

    // Encryption support
    private IClientEncryptor? _encryptor;
    private bool _encryptionEnabled;
    private readonly SemaphoreSlim _encryptionInitLock = new(1, 1);

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
        _accountHub = await GetOrCreateHubAsync(_accountHub, "/hub/account");
        return _accountHub.CreateHubProxy<IAccountHubClient>();
    }

    /// <summary>
    /// Gets a typed Character hub client.
    /// </summary>
    public async Task<ICharacterHubClient> GetCharacterClientAsync()
    {
        _characterHub = await GetOrCreateHubAsync(_characterHub, "/hub/character");
        return _characterHub.CreateHubProxy<ICharacterHubClient>();
    }

    /// <summary>
    /// Gets a typed Inventory hub client.
    /// </summary>
    public async Task<IInventoryHubClient> GetInventoryClientAsync()
    {
        _inventoryHub = await GetOrCreateHubAsync(_inventoryHub, "/hub/inventory");
        return _inventoryHub.CreateHubProxy<IInventoryHubClient>();
    }

    /// <summary>
    /// Gets a typed Trade hub client and optionally registers for callbacks.
    /// </summary>
    /// <param name="receiver">Optional receiver for trade update callbacks.</param>
    public async Task<ITradeHubClient> GetTradeClientAsync(ITradeHubReceiver? receiver = null)
    {
        _tradeHub = await GetOrCreateHubAsync(_tradeHub, "/hub/trade");

        if (receiver != null)
        {
            _tradeHub.Register(receiver);
        }

        return _tradeHub.CreateHubProxy<ITradeHubClient>();
    }

    /// <summary>
    /// Gets a typed BaseType hub client.
    /// </summary>
    public async Task<IBaseTypeHubClient> GetBaseTypeClientAsync()
    {
        _baseTypeHub = await GetOrCreateHubAsync(_baseTypeHub, "/hub/base-type");
        return _baseTypeHub.CreateHubProxy<IBaseTypeHubClient>();
    }

    /// <summary>
    /// Gets a typed Season hub client.
    /// </summary>
    public async Task<ISeasonHubClient> GetSeasonClientAsync()
    {
        _seasonHub = await GetOrCreateHubAsync(_seasonHub, "/hub/season");
        return _seasonHub.CreateHubProxy<ISeasonHubClient>();
    }

    /// <summary>
    /// Gets a typed Auth hub client for WebSocket-based auth operations.
    /// </summary>
    public async Task<IAuthHubClient> GetAuthHubClientAsync()
    {
        _authHub = await GetOrCreateHubAsync(_authHub, "/hub/auth");
        return _authHub.CreateHubProxy<IAuthHubClient>();
    }

    /// <summary>
    /// Gets the raw HubConnection for a specific hub path.
    /// Useful for advanced scenarios like registering custom callbacks.
    /// </summary>
    public async Task<HubConnection> GetHubConnectionAsync(string hubPath)
    {
        return await CreateAndConnectHubAsync(hubPath);
    }

    /// <summary>
    /// Thread-safe helper to get or create a hub connection.
    /// </summary>
    private async Task<HubConnection> GetOrCreateHubAsync(HubConnection? existing, string hubPath)
    {
        if (existing != null)
            return existing;

        await _hubInitLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (existing != null)
                return existing;

            return await CreateAndConnectHubAsync(hubPath);
        }
        finally
        {
            _hubInitLock.Release();
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

        await connection.StartAsync();
        _logger?.LogDebug("Connected to {HubPath}", hubPath);

        // Perform key exchange if encryption is enabled
        if (_options.EnablePayloadEncryption && !_encryptionEnabled)
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
        // Connect to the encryption hub
        _encryptionHub = new HubConnectionBuilder()
            .WithUrl($"{_options.BaseUrl}/hub/encryption", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_currentSessionId);
            })
            .Build();

        // Register for key rotation messages
        _encryptionHub.On<KeyRotationRequest>("KeyRotation", async request =>
        {
            try
            {
                if (_encryptor != null)
                {
                    var ack = _encryptor.HandleRotationRequest(request);
                    await _encryptionHub.InvokeAsync("CompleteKeyRotation", ack);
                    _logger?.LogDebug("Completed key rotation, new KeyId: {KeyId}", request.KeyId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Key rotation failed for KeyId: {KeyId}", request.KeyId);
            }
        });

        await _encryptionHub.StartAsync();
        _logger?.LogDebug("Connected to encryptionHub");

        // Perform key exchange
        _encryptor = new ClientEncryptor();
        
        var success = await _encryptor.PerformKeyExchangeAsync(async request =>
        {
            // Send key exchange request to server and get response
            return await _encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", request);
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
        // Dispose encryptor first (cleans up cryptographic key material securely)
        // This ensures no pending encryption operations are running before hub shutdown
        (_encryptor as IDisposable)?.Dispose();
        
        var hubs = new[] { _accountHub, _characterHub, _inventoryHub, _tradeHub, _baseTypeHub, _seasonHub, _authHub, _encryptionHub };

        foreach (var hub in hubs)
        {
            if (hub != null)
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
        }

        // Dispose the initialization locks
        _encryptionInitLock.Dispose();
        _hubInitLock.Dispose();
        
        _httpClient.Dispose();
        _httpHandler.Dispose();
    }
}
