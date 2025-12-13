using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Identity;

/// <summary>
/// In-memory presence tracking grain.
/// Tracks active connections and online status without persistence.
/// </summary>
public class PlayerPresenceGrain : Grain, IPlayerPresenceGrain
{
    private readonly Dictionary<string, PlayerSession> _connections = new();
    private DateTimeOffset _lastSeen = DateTimeOffset.UtcNow;
    private string? _currentActivity;

    public Task RegisterConnectionAsync(string connectionId, string hubName)
    {
        _connections[connectionId] = new PlayerSession
        {
            ConnectionId = connectionId,
            UserId = this.GetPrimaryKey(),
            ConnectedAt = DateTimeOffset.UtcNow,
            HubName = hubName
        };
        _lastSeen = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task UnregisterConnectionAsync(string connectionId)
    {
        _connections.Remove(connectionId);
        _lastSeen = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task<PlayerPresence> GetPresenceAsync()
    {
        return Task.FromResult(new PlayerPresence
        {
            UserId = this.GetPrimaryKey(),
            IsOnline = _connections.Count > 0,
            ConnectionCount = _connections.Count,
            LastSeen = _lastSeen,
            CurrentActivity = _currentActivity
        });
    }

    public Task<bool> IsOnlineAsync()
    {
        return Task.FromResult(_connections.Count > 0);
    }

    public Task<int> GetConnectionCountAsync()
    {
        return Task.FromResult(_connections.Count);
    }

    public Task SetActivityAsync(string activity)
    {
        _currentActivity = activity;
        return Task.CompletedTask;
    }
}
