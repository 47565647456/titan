using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Titan.Abstractions.Contracts;
using TypedSignalR.Client;

namespace Titan.Client;

/// <summary>
/// Strongly-typed Titan API client.
/// Provides compile-time safe access to all hub operations.
/// </summary>
public sealed class TitanClient : IAsyncDisposable
{
    private readonly TitanClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TitanClient>? _logger;

    private HubConnection? _accountHub;
    private HubConnection? _characterHub;
    private HubConnection? _inventoryHub;
    private HubConnection? _tradeHub;
    private HubConnection? _itemTypeHub;
    private HubConnection? _seasonHub;
    private HubConnection? _authHub;

    private string? _currentAccessToken;
    private Guid? _currentUserId;

    /// <summary>
    /// HTTP authentication client.
    /// </summary>
    public IAuthClient Auth { get; }

    /// <summary>
    /// Current user ID after successful login.
    /// </summary>
    public Guid? UserId => _currentUserId;

    /// <summary>
    /// Current access token after successful login.
    /// </summary>
    public string? AccessToken => _currentAccessToken;

    /// <summary>
    /// Whether the client is authenticated.
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_currentAccessToken);

    /// <summary>
    /// Creates a new TitanClient with the specified options.
    /// Use TitanClientBuilder for a fluent configuration API.
    /// </summary>
    public TitanClient(TitanClientOptions options)
    {
        _options = options;
        _httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        _logger = options.LoggerFactory?.CreateLogger<TitanClient>();
        Auth = new AuthClient(_httpClient, this);
    }

    /// <summary>
    /// Sets the authentication state after login.
    /// Called internally by AuthClient.
    /// </summary>
    internal void SetAuthState(string accessToken, Guid userId)
    {
        _currentAccessToken = accessToken;
        _currentUserId = userId;
        _logger?.LogDebug("Authentication state updated for user {UserId}", userId);
    }

    /// <summary>
    /// Gets a typed Account hub client.
    /// </summary>
    public async Task<IAccountHubClient> GetAccountClientAsync()
    {
        _accountHub ??= await CreateAndConnectHubAsync("/accountHub");
        return _accountHub.CreateHubProxy<IAccountHubClient>();
    }

    /// <summary>
    /// Gets a typed Character hub client.
    /// </summary>
    public async Task<ICharacterHubClient> GetCharacterClientAsync()
    {
        _characterHub ??= await CreateAndConnectHubAsync("/characterHub");
        return _characterHub.CreateHubProxy<ICharacterHubClient>();
    }

    /// <summary>
    /// Gets a typed Inventory hub client.
    /// </summary>
    public async Task<IInventoryHubClient> GetInventoryClientAsync()
    {
        _inventoryHub ??= await CreateAndConnectHubAsync("/inventoryHub");
        return _inventoryHub.CreateHubProxy<IInventoryHubClient>();
    }

    /// <summary>
    /// Gets a typed Trade hub client and optionally registers for callbacks.
    /// </summary>
    /// <param name="receiver">Optional receiver for trade update callbacks.</param>
    public async Task<ITradeHubClient> GetTradeClientAsync(ITradeHubReceiver? receiver = null)
    {
        _tradeHub ??= await CreateAndConnectHubAsync("/tradeHub");

        if (receiver != null)
        {
            _tradeHub.Register(receiver);
        }

        return _tradeHub.CreateHubProxy<ITradeHubClient>();
    }

    /// <summary>
    /// Gets a typed ItemType hub client.
    /// </summary>
    public async Task<IItemTypeHubClient> GetItemTypeClientAsync()
    {
        _itemTypeHub ??= await CreateAndConnectHubAsync("/itemTypeHub");
        return _itemTypeHub.CreateHubProxy<IItemTypeHubClient>();
    }

    /// <summary>
    /// Gets a typed Season hub client.
    /// </summary>
    public async Task<ISeasonHubClient> GetSeasonClientAsync()
    {
        _seasonHub ??= await CreateAndConnectHubAsync("/seasonHub");
        return _seasonHub.CreateHubProxy<ISeasonHubClient>();
    }

    /// <summary>
    /// Gets a typed Auth hub client for WebSocket-based auth operations.
    /// </summary>
    public async Task<IAuthHubClient> GetAuthHubClientAsync()
    {
        _authHub ??= await CreateAndConnectHubAsync("/authHub");
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

    private async Task<HubConnection> CreateAndConnectHubAsync(string hubPath)
    {
        if (string.IsNullOrEmpty(_currentAccessToken))
            throw new InvalidOperationException("Client is not authenticated. Call Auth.LoginAsync first.");

        var builder = new HubConnectionBuilder()
            .WithUrl($"{_options.BaseUrl}{hubPath}", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_currentAccessToken);
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

        return connection;
    }

    /// <summary>
    /// Disconnects from all hubs and disposes resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var hubs = new[] { _accountHub, _characterHub, _inventoryHub, _tradeHub, _itemTypeHub, _seasonHub, _authHub };

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

        _httpClient.Dispose();
    }
}
