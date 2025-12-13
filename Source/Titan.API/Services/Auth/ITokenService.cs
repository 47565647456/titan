namespace Titan.API.Services.Auth;

/// <summary>
/// Service for generating JWT access tokens for authenticated users.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a signed JWT access token for the specified user.
    /// </summary>
    /// <param name="userId">The user's internal ID.</param>
    /// <param name="provider">The authentication provider (e.g., "EOS", "Mock").</param>
    /// <param name="roles">Optional roles to include in the token.</param>
    /// <returns>A signed JWT access token string.</returns>
    string GenerateAccessToken(Guid userId, string provider, IEnumerable<string>? roles = null);

    /// <summary>
    /// Gets the configured expiration time for access tokens.
    /// Used by clients to schedule token refreshes.
    /// </summary>
    TimeSpan AccessTokenExpiration { get; }

    /// <summary>
    /// Gets the configured expiration time for refresh tokens.
    /// Used by grains when creating refresh tokens.
    /// </summary>
    TimeSpan RefreshTokenExpiration { get; }
}
