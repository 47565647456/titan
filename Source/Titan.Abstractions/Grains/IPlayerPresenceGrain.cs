using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for tracking player online presence (in-memory, not persisted).
/// Key: UserId (Guid)
/// </summary>
public interface IPlayerPresenceGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Register a new connection for this user.
    /// </summary>
    Task RegisterConnectionAsync(string connectionId, string hubName);

    /// <summary>
    /// Unregister a connection when it closes.
    /// </summary>
    Task UnregisterConnectionAsync(string connectionId);

    /// <summary>
    /// Get the current presence status.
    /// </summary>
    Task<PlayerPresence> GetPresenceAsync();

    /// <summary>
    /// Check if the user is currently online.
    /// </summary>
    Task<bool> IsOnlineAsync();

    /// <summary>
    /// Get the number of active connections.
    /// </summary>
    Task<int> GetConnectionCountAsync();

    /// <summary>
    /// Set the user's current activity (e.g., "idle", "trading", "in-game").
    /// </summary>
    Task SetActivityAsync(string activity);
}
