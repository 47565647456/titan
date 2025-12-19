using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Titan.LoadTests.Infrastructure;

/// <summary>
/// Pool of pre-authenticated TitanClients for load testing.
/// Clients are created upfront and reused across iterations.
/// </summary>
public class TitanClientPool : IAsyncDisposable
{
    private readonly ConcurrentBag<TitanClient> _availableClients = new();
    private readonly ConcurrentBag<TitanClient> _allClients = new();
    private readonly string _baseUrl;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    
    public int TotalClients => _allClients.Count;
    public int AvailableClients => _availableClients.Count;
    
    public TitanClientPool(string baseUrl)
    {
        _baseUrl = baseUrl;
    }
    
    /// <summary>
    /// Initialize the pool with pre-authenticated clients.
    /// </summary>
    public async Task InitializeAsync(int clientCount, CancellationToken ct = default)
    {
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            
            Console.WriteLine($"[Pool] Creating {clientCount} pre-authenticated clients...");
            
            var tasks = new List<Task<TitanClient?>>();
            for (int i = 0; i < clientCount; i++)
            {
                tasks.Add(CreateAuthenticatedClientAsync(ct));
            }
            
            var clients = await Task.WhenAll(tasks);
            
            int successCount = 0;
            foreach (var client in clients)
            {
                if (client != null)
                {
                    _allClients.Add(client);
                    _availableClients.Add(client);
                    successCount++;
                }
            }
            
            Console.WriteLine($"[Pool] Created {successCount}/{clientCount} clients successfully");
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
    
    private async Task<TitanClient?> CreateAuthenticatedClientAsync(CancellationToken ct)
    {
        try
        {
            var client = new TitanClient(_baseUrl);
            var success = await client.LoginAsync("", ct);
            return success ? client : null;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Rent a client from the pool. Returns null if none available.
    /// </summary>
    public TitanClient? Rent()
    {
        return _availableClients.TryTake(out var client) ? client : null;
    }
    
    /// <summary>
    /// Return a client to the pool.
    /// </summary>
    public void Return(TitanClient client)
    {
        _availableClients.Add(client);
    }
    
    /// <summary>
    /// Rent two clients for trading scenarios.
    /// </summary>
    public (TitanClient? User1, TitanClient? User2) RentPair()
    {
        var user1 = Rent();
        var user2 = Rent();
        return (user1, user2);
    }
    
    /// <summary>
    /// Return a pair of clients.
    /// </summary>
    public void ReturnPair(TitanClient? user1, TitanClient? user2)
    {
        if (user1 != null) Return(user1);
        if (user2 != null) Return(user2);
    }
    
    public async ValueTask DisposeAsync()
    {
        foreach (var client in _allClients)
        {
            try { await client.DisposeAsync(); } catch { }
        }
        _allClients.Clear();
        _initLock.Dispose();
    }
}

/// <summary>
/// Manages authentication and hub connections for a virtual user.
/// Optimized for connection pooling - connections are kept alive.
/// </summary>
public class TitanClient : IAsyncDisposable
{
    private static readonly HttpClient SharedHttpClient;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    
    private readonly string _baseUrl;
    private readonly Dictionary<string, HubConnection> _connections = new();
    private readonly SemaphoreSlim _hubLock = new(1, 1);
    
    public string? AccessToken { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);
    
    static TitanClient()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 1000,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        
        SharedHttpClient = new HttpClient(handler)
        {
            Timeout = DefaultTimeout
        };
    }
    
    public TitanClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }
    
    public async Task<bool> LoginAsync(string mockToken = "", CancellationToken ct = default)
    {
        var result = await LoginWithRateLimitInfoAsync(mockToken, ct);
        return result.Success;
    }
    
    public async Task<LoginRateLimitResult> LoginWithRateLimitInfoAsync(string mockToken = "", CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(mockToken))
                mockToken = $"mock:{Guid.NewGuid()}";
            
            var request = new { token = mockToken, provider = "Mock" };
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeout);
            
            var response = await SharedHttpClient.PostAsJsonAsync($"{_baseUrl}/api/auth/login", request, cts.Token);
            
            // Handle rate limiting
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = GetRetryAfter(response);
                return new LoginRateLimitResult(false, true, retryAfter);
            }
            
            if (!response.IsSuccessStatusCode)
                return new LoginRateLimitResult(false, false, null);
            
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cts.Token);
            if (result?.Success != true || string.IsNullOrEmpty(result.AccessToken))
                return new LoginRateLimitResult(false, false, null);
            
            AccessToken = result.AccessToken;
            UserId = result.UserId ?? Guid.Empty;
            return new LoginRateLimitResult(true, false, null);
        }
        catch
        {
            return new LoginRateLimitResult(false, false, null);
        }
    }
    
    /// <summary>
    /// Makes a GET request with rate limit handling.
    /// </summary>
    public async Task<RateLimitedResult<T>> GetAsync<T>(string path, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeout);
            
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{path}");
            if (!string.IsNullOrEmpty(AccessToken))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);
            
            var response = await SharedHttpClient.SendAsync(request, cts.Token);
            
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = GetRetryAfter(response);
                return new RateLimitedResult<T>(default, false, true, retryAfter);
            }
            
            if (!response.IsSuccessStatusCode)
                return new RateLimitedResult<T>(default, false, false, null);
            
            var result = await response.Content.ReadFromJsonAsync<T>(cts.Token);
            return new RateLimitedResult<T>(result, true, false, null);
        }
        catch
        {
            return new RateLimitedResult<T>(default, false, false, null);
        }
    }
    
    private static int? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var value = values.FirstOrDefault();
            if (int.TryParse(value, out var seconds))
                return seconds;
        }
        return null;
    }
    
    /// <summary>
    /// Fetches a short-lived connection ticket for WebSocket authentication.
    /// </summary>
    private async Task<string?> GetConnectionTicketAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeout);
            
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/auth/connection-ticket");
            if (!string.IsNullOrEmpty(AccessToken))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);
            
            var response = await SharedHttpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
                return null;
            
            var result = await response.Content.ReadFromJsonAsync<TicketResponse>(cts.Token);
            return result?.Ticket;
        }
        catch
        {
            return null;
        }
    }
    
    public async Task<HubConnection> GetHubAsync(string hubPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(AccessToken))
            throw new InvalidOperationException("Must login before accessing hubs");
        
        await _hubLock.WaitAsync(ct);
        try
        {
            if (_connections.TryGetValue(hubPath, out var existing))
            {
                if (existing.State == HubConnectionState.Connected)
                    return existing;
                
                try { await existing.DisposeAsync(); } catch { }
                _connections.Remove(hubPath);
            }
            
            // Get a connection ticket for this hub
            var ticket = await GetConnectionTicketAsync(ct)
                ?? throw new InvalidOperationException("Failed to get connection ticket");
            
            var connection = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}{hubPath}?ticket={Uri.EscapeDataString(ticket)}")
                .Build();
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            
            await connection.StartAsync(cts.Token);
            _connections[hubPath] = connection;
            return connection;
        }
        finally
        {
            _hubLock.Release();
        }
    }
    
    private record TicketResponse(string Ticket);
    
    public Task<HubConnection> GetAccountHubAsync(CancellationToken ct = default) => GetHubAsync("/accountHub", ct);
    public Task<HubConnection> GetInventoryHubAsync(CancellationToken ct = default) => GetHubAsync("/inventoryHub", ct);
    public Task<HubConnection> GetTradeHubAsync(CancellationToken ct = default) => GetHubAsync("/tradeHub", ct);
    
    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values)
        {
            try { await connection.DisposeAsync(); } catch { }
        }
        _connections.Clear();
        _hubLock.Dispose();
    }
}

internal record LoginResponse(
    bool Success, 
    string? AccessToken, 
    string? RefreshToken, 
    int? AccessTokenExpiresInSeconds, 
    Guid? UserId
);

/// <summary>
/// Result of a login attempt with rate limit information.
/// </summary>
public record LoginRateLimitResult(
    bool Success,
    bool RateLimited,
    int? RetryAfterSeconds
);

/// <summary>
/// Result of an operation that may be rate limited.
/// </summary>
public record RateLimitedResult<T>(
    T? Data,
    bool Success,
    bool RateLimited,
    int? RetryAfterSeconds
);
