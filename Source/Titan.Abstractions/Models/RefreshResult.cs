namespace Titan.Abstractions.Models;

/// <summary>
/// Result of a token refresh operation.
/// </summary>
/// <param name="AccessToken">The new JWT access token.</param>
/// <param name="RefreshToken">The new refresh token (rotated).</param>
/// <param name="AccessTokenExpiresInSeconds">New access token expiration time in seconds.</param>
public record RefreshResult(
    string AccessToken,
    string RefreshToken,
    int AccessTokenExpiresInSeconds);
