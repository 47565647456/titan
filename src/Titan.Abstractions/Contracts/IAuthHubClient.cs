using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for AuthHub operations.
/// Used with TypedSignalR.Client source generator.
/// Note: Login is handled via HTTP. This hub provides WebSocket-based auth utilities.
/// </summary>
public interface IAuthHubClient
{
    /// <summary>
    /// Refresh access token using a valid refresh token.
    /// Called over existing WebSocket connection - no reconnection needed.
    /// </summary>
    Task<RefreshResult> RefreshToken(string refreshToken);

    /// <summary>
    /// Logout and revoke the refresh token.
    /// </summary>
    Task Logout(string refreshToken);

    /// <summary>
    /// Revoke all refresh tokens for the current user (e.g., security event).
    /// </summary>
    Task RevokeAllTokens();

    /// <summary>
    /// Get current user's profile.
    /// </summary>
    Task<UserProfile> GetProfile();
}
