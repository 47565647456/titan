using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for AuthHub operations.
/// Used with TypedSignalR.Client source generator.
/// Note: Login is handled via HTTP. This hub provides WebSocket-based session utilities.
/// </summary>
public interface IAuthHubClient
{
    /// <summary>
    /// Logout and invalidate the current session.
    /// </summary>
    Task Logout();

    /// <summary>
    /// Revoke all sessions for the current user (e.g., security event).
    /// </summary>
    /// <returns>Number of sessions revoked.</returns>
    Task<int> RevokeAllSessions();

    /// <summary>
    /// Get current user's profile.
    /// </summary>
    Task<UserProfile> GetProfile();
}
