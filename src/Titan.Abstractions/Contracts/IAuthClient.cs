using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// HTTP authentication client contract.
/// Used by Titan.Client for login/logout operations via HTTP.
/// Session-based authentication.
/// </summary>
public interface IAuthClient
{
    /// <summary>
    /// Authenticate with a provider token.
    /// </summary>
    /// <param name="token">The authentication token from the provider.</param>
    /// <param name="provider">The provider name (default: "EOS").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Login response with session info.</returns>
    Task<LoginResponse> LoginAsync(string token, string provider = "EOS", CancellationToken ct = default);

    /// <summary>
    /// Logout and invalidate the current session.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the session existed and was successfully invalidated.</returns>
    Task<bool> LogoutAsync(CancellationToken ct = default);

    /// <summary>
    /// Logout and invalidate all sessions for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of sessions invalidated.</returns>
    Task<int> LogoutAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Get available authentication providers.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of provider names.</returns>
    Task<IReadOnlyList<string>> GetProvidersAsync(CancellationToken ct = default);
}

/// <summary>
/// Login response with session ticket and user info.
/// </summary>
public record LoginResponse(
    bool Success,
    Guid? UserId,
    string? Provider,
    UserIdentity? Identity,
    string? SessionId,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Basic response for logout.
/// </summary>
public record LogoutResponse(bool Success, bool SessionInvalidated);

/// <summary>
/// Response for logout all.
/// </summary>
public record LogoutAllResult(int SessionsInvalidated);

/// <summary>
/// Response from the admin login endpoint.
/// </summary>
public record AdminLoginResponse(
    bool Success,
    Guid UserId,
    string Email,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    string SessionId,
    DateTimeOffset ExpiresAt);
