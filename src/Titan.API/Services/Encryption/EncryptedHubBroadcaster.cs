using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Titan.Abstractions.Models;
using Titan.API.Config;

namespace Titan.API.Services.Encryption;

/// <summary>
/// Provides encrypted broadcasting for SignalR hubs.
/// Tracks connection-to-user mappings and encrypts messages per-user.
/// Use this for background pushes where IHubProtocol can't access user context.
/// </summary>
public class EncryptedHubBroadcaster<THub> where THub : Hub
{
    private readonly IHubContext<THub> _hubContext;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<EncryptedHubBroadcaster<THub>> _logger;
    private readonly int _broadcastMaxConcurrency;

    // Track which connections belong to which users
    private readonly ConcurrentDictionary<string, string> _connectionToUser = new();
    
    // Track which connections are in which groups
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _groupConnections = new();

    public EncryptedHubBroadcaster(
        IHubContext<THub> hubContext,
        IEncryptionService encryptionService,
        IOptions<EncryptionOptions> options,
        ILogger<EncryptedHubBroadcaster<THub>> logger)
    {
        _hubContext = hubContext;
        _encryptionService = encryptionService;
        _broadcastMaxConcurrency = options.Value.BroadcastMaxConcurrency;
        _logger = logger;
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Register a connection with its user ID. Call this when a client connects.
    /// </summary>
    public void RegisterConnection(string connectionId, string userId)
    {
        _connectionToUser[connectionId] = userId;
        _logger.LogDebug("Registered connection {ConnectionId} for user {UserId}", connectionId, userId);
    }

    /// <summary>
    /// Unregister a connection. Call this when a client disconnects.
    /// </summary>
    public void UnregisterConnection(string connectionId)
    {
        _connectionToUser.TryRemove(connectionId, out _);
        
        // Remove from all groups
        foreach (var group in _groupConnections.Values)
        {
            group.TryRemove(connectionId, out _);
        }
        
        _logger.LogDebug("Unregistered connection {ConnectionId}", connectionId);
    }

    /// <summary>
    /// Add a connection to a group for encrypted broadcasting.
    /// </summary>
    public void AddToGroup(string connectionId, string groupName)
    {
        var group = _groupConnections.GetOrAdd(groupName, _ => new ConcurrentDictionary<string, bool>());
        group[connectionId] = true;
        _logger.LogDebug("Added connection {ConnectionId} to group {Group}", connectionId, groupName);
    }

    /// <summary>
    /// Remove a connection from a group.
    /// </summary>
    public void RemoveFromGroup(string connectionId, string groupName)
    {
        if (_groupConnections.TryGetValue(groupName, out var group))
        {
            group.TryRemove(connectionId, out _);
        }
        _logger.LogDebug("Removed connection {ConnectionId} from group {Group}", connectionId, groupName);
    }

    /// <summary>
    /// Send an encrypted message to all connections in a group.
    /// Uses parallel execution with concurrency limits for scalability.
    /// </summary>
    public async Task SendToGroupAsync<T>(string groupName, string method, T data)
    {
        if (!_groupConnections.TryGetValue(groupName, out var group))
        {
            _logger.LogDebug("No connections in group {Group}", groupName);
            return;
        }

        var config = _encryptionService.GetConfig();
        var connections = group.Keys.ToList();
        
        _logger.LogDebug("Broadcasting {Method} to {Count} connections in group {Group}", 
            method, connections.Count, groupName);

        // Process connections in parallel with configurable concurrency limit
        foreach (var batch in connections.Chunk(_broadcastMaxConcurrency))
        {
            var tasks = batch.Select(connectionId => SendToSingleConnectionInGroupAsync(
                connectionId, method, data, config));
            
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Helper method to send to a single connection within a group broadcast.
    /// Isolated for parallel execution with per-connection error handling.
    /// </summary>
    private async Task SendToSingleConnectionInGroupAsync<T>(
        string connectionId, 
        string method, 
        T data, 
        EncryptionConfig config)
    {
        try
        {
            if (!_connectionToUser.TryGetValue(connectionId, out var userId))
            {
                _logger.LogDebug("Connection {ConnectionId} has no registered user", connectionId);
                return;
            }

            if (config.Enabled && _encryptionService.IsEncryptionEnabled(userId))
            {
                var envelope = await EncryptForUserAsync(userId, data);
                await _hubContext.Clients.Client(connectionId).SendAsync(method, envelope);
            }
            else
            {
                if (config.Required)
                {
                    _logger.LogWarning("Dropping broadcast to connection {ConnectionId} (user {UserId}) because strict encryption is required but not established", 
                        connectionId, userId);
                    return;
                }

                await _hubContext.Clients.Client(connectionId).SendAsync(method, data);
                _logger.LogDebug("Sent plaintext {Method} to connection {ConnectionId}", method, connectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send {Method} to connection {ConnectionId}", method, connectionId);
        }
    }

    /// <summary>
    /// Send an encrypted message to a specific connection.
    /// Encrypts if the user has encryption enabled.
    /// </summary>
    public async Task SendToConnectionAsync<T>(string connectionId, string method, T data)
    {
        try
        {
            if (!_connectionToUser.TryGetValue(connectionId, out var userId))
            {
                var cfg = _encryptionService.GetConfig();
                if (cfg.Required)
                {
                    _logger.LogWarning("Dropping message to unregistered connection {ConnectionId} - encryption required", connectionId);
                    return;
                }
                _logger.LogDebug("Connection {ConnectionId} has no registered user, sending plaintext", connectionId);
                await _hubContext.Clients.Client(connectionId).SendAsync(method, data);
                return;
            }

            var config = _encryptionService.GetConfig();
            if (config.Enabled && _encryptionService.IsEncryptionEnabled(userId))
            {
                // Encrypt for this user
                var envelope = await EncryptForUserAsync(userId, data);
                await _hubContext.Clients.Client(connectionId).SendAsync(method, envelope);
                _logger.LogDebug("Sent encrypted {Method} to connection {ConnectionId}", method, connectionId);
            }
            else
            {
                if (config.Required)
                {
                    _logger.LogWarning("Dropping message to connection {ConnectionId} because encryption is required but not active", connectionId);
                    return;
                }

                await _hubContext.Clients.Client(connectionId).SendAsync(method, data);
                _logger.LogDebug("Sent plaintext {Method} to connection {ConnectionId}", method, connectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send {Method} to connection {ConnectionId}", method, connectionId);
        }
    }

    private async Task<SecureEnvelope> EncryptForUserAsync<T>(string userId, T data)
    {
        // Serialize the data payload directly
        var json = JsonSerializer.Serialize(data, s_jsonOptions);
        
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(json);
        return await _encryptionService.EncryptAndSignAsync(userId, plainBytes);
    }
}
