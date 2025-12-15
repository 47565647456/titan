using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services.Auth;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for authentication operations.
/// Login is handled via HTTP (POST /api/auth/login) following industry standards.
/// This hub provides token refresh over existing WebSocket connections and other auth utilities.
/// </summary>
public class AuthHub : Hub
{
    private readonly IClusterClient _clusterClient;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthHub> _logger;

    public AuthHub(
        IClusterClient clusterClient, 
        ITokenService tokenService,
        ILogger<AuthHub> logger)
    {
        _clusterClient = clusterClient;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// Refresh access token using a valid refresh token.
    /// Called over existing WebSocket connection - no reconnection needed.
    /// Returns new access + refresh tokens (rotation).
    /// Requires authentication since we use Context.UserIdentifier.
    /// </summary>
    /// <param name="refreshToken">The refresh token ID from a previous login or refresh.</param>
    [Authorize]
    public async Task<RefreshResult> RefreshToken(string refreshToken)
    {
        var userId = Guid.Parse(Context.UserIdentifier!);
        var grain = _clusterClient.GetGrain<IRefreshTokenGrain>(userId);
        var tokenInfo = await grain.ConsumeTokenAsync(refreshToken);

        if (tokenInfo == null)
        {
            _logger.LogWarning("Refresh token invalid or expired for user {UserId}", userId);
            throw new HubException("Invalid or expired refresh token.");
        }

        // Generate new access token with stored roles
        var accessToken = _tokenService.GenerateAccessToken(userId, tokenInfo.Provider, tokenInfo.Roles);
        var expiresInSeconds = (int)_tokenService.AccessTokenExpiration.TotalSeconds;

        // Generate new refresh token (rotation)
        var newRefreshTokenInfo = await grain.CreateTokenAsync(tokenInfo.Provider, tokenInfo.Roles);

        _logger.LogDebug("Token refreshed for user {UserId}", userId);

        return new RefreshResult(
            AccessToken: accessToken,
            RefreshToken: newRefreshTokenInfo.TokenId,
            AccessTokenExpiresInSeconds: expiresInSeconds);
    }

    /// <summary>
    /// Logout and revoke the refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token to revoke.</param>
    [Authorize]
    public async Task Logout(string refreshToken)
    {
        var userId = Guid.Parse(Context.UserIdentifier!);
        var grain = _clusterClient.GetGrain<IRefreshTokenGrain>(userId);
        await grain.RevokeTokenAsync(refreshToken);
        
        _logger.LogInformation("User {UserId} logged out, refresh token revoked", userId);
    }

    /// <summary>
    /// Revoke all refresh tokens for the current user (e.g., security event).
    /// </summary>
    [Authorize]
    public async Task RevokeAllTokens()
    {
        var userId = Guid.Parse(Context.UserIdentifier!);
        var grain = _clusterClient.GetGrain<IRefreshTokenGrain>(userId);
        await grain.RevokeAllTokensAsync();
        
        _logger.LogInformation("User {UserId} revoked all refresh tokens", userId);
    }

    /// <summary>
    /// Get current user's profile (requires authentication).
    /// </summary>
    [Authorize]
    public async Task<UserProfile> GetProfile()
    {
        var userId = Guid.Parse(Context.UserIdentifier!);
        var grain = _clusterClient.GetGrain<IUserProfileGrain>(userId);
        return await grain.GetProfileAsync();
    }
}
