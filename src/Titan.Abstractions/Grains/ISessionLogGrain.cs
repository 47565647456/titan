using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for tracking session login/logout history (persisted to DB).
/// Key: UserId (Guid)
/// </summary>
public interface ISessionLogGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Start a new session when the user connects.
    /// Returns the new session ID.
    /// </summary>
    Task<Guid> StartSessionAsync(string? ipAddress);

    /// <summary>
    /// End the current session when the user disconnects.
    /// Records logout time and calculates duration.
    /// </summary>
    Task EndSessionAsync();

    /// <summary>
    /// Get recent session history for this user.
    /// </summary>
    Task<IReadOnlyList<SessionLog>> GetRecentSessionsAsync(int count = 10);
}
