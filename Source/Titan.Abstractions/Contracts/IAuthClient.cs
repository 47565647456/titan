using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// HTTP authentication client contract.
/// Used by Titan.Client for login/logout operations via HTTP.
/// </summary>
public interface IAuthClient
{
    /// <summary>
    /// Authenticate with a provider token.
    /// </summary>
    /// <param name="token">The authentication token from the provider.</param>
    /// <param name="provider">The provider name (default: "EOS").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Login response with tokens and user info.</returns>
    Task<LoginResponse> LoginAsync(string token, string provider = "EOS", CancellationToken ct = default);

    /// <summary>
    /// Refresh access token using a valid refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token from a previous login or refresh.</param>
    /// <param name="userId">The user ID associated with the refresh token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>New access and refresh tokens.</returns>
    Task<RefreshResult> RefreshAsync(string refreshToken, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Logout and revoke the refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Get available authentication providers.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of provider names.</returns>
    Task<IReadOnlyList<string>> GetProvidersAsync(CancellationToken ct = default);
}

/// <summary>
/// Login response with tokens and user info.
/// </summary>
public record LoginResponse(
    bool Success,
    Guid? UserId,
    string? Provider,
    UserIdentity? Identity,
    string? AccessToken,
    string? RefreshToken,
    int? AccessTokenExpiresInSeconds);
