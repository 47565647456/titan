using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing refresh tokens. Keyed by UserId.
/// Stores active refresh tokens and supports rotation/revocation.
/// </summary>
public interface IRefreshTokenGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Creates a new refresh token. Returns the token info including the token ID.
    /// </summary>
    Task<RefreshTokenInfo> CreateTokenAsync(string provider, IReadOnlyList<string> roles);

    /// <summary>
    /// Validates and consumes a refresh token (rotation).
    /// Returns the stored metadata if valid, null if invalid/expired/revoked.
    /// Consuming a token removes it from storage (rotation).
    /// </summary>
    Task<RefreshTokenInfo?> ConsumeTokenAsync(string tokenId);

    /// <summary>
    /// Revokes a specific token by ID.
    /// </summary>
    Task RevokeTokenAsync(string tokenId);

    /// <summary>
    /// Revokes all tokens for this user (e.g., password change, security event).
    /// </summary>
    Task RevokeAllTokensAsync();

    /// <summary>
    /// Gets the count of active tokens for this user.
    /// </summary>
    Task<int> GetActiveTokenCountAsync();
}
