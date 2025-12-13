using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services.Auth;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for authentication operations.
/// This hub is intentionally NOT protected by [Authorize] to allow login and token refresh.
/// </summary>
public class AuthHub : Hub
{
    private readonly IAuthServiceFactory _authServiceFactory;
    private readonly IClusterClient _clusterClient;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthHub> _logger;

    public AuthHub(
        IAuthServiceFactory authServiceFactory, 
        IClusterClient clusterClient, 
        ITokenService tokenService,
        ILogger<AuthHub> logger)
    {
        _authServiceFactory = authServiceFactory;
        _clusterClient = clusterClient;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// Authenticate with a provider token. Returns user info, access token, and refresh token.
    /// 
    /// For EOS: Send the ID Token received from EOS Connect SDK.
    /// For Mock (dev only): Use format "mock:{guid}" or "mock:admin:{guid}".
    /// </summary>
    /// <param name="token">The authentication token from the provider.</param>
    /// <param name="provider">The provider name. Default: "EOS". Use "Mock" for development.</param>
    public async Task<LoginResult> Login(string token, string provider = "EOS")
    {
        _logger.LogInformation("Login attempt via provider: {Provider}", provider);

        // Validate provider exists
        if (!_authServiceFactory.HasProvider(provider))
        {
            var available = string.Join(", ", _authServiceFactory.GetProviderNames());
            return LoginResult.Failed($"Unknown provider '{provider}'. Available: {available}");
        }

        var authService = _authServiceFactory.GetService(provider);
        var result = await authService.ValidateTokenAsync(token);
        
        if (!result.Success)
        {
            _logger.LogWarning("Login failed for provider {Provider}: {Error}", 
                provider, result.ErrorMessage);
            return LoginResult.Failed(result.ErrorMessage);
        }

        // Ensure user identity grain exists and link provider
        var identityGrain = _clusterClient.GetGrain<IUserIdentityGrain>(result.UserId!.Value);
        await identityGrain.LinkProviderAsync(result.ProviderName!, result.ExternalId!);
        var identity = await identityGrain.GetIdentityAsync();

        // Determine roles
        var roles = new List<string> { "User" };
        
        // Admin backdoor for development: "mock:admin:{guid}"
        if (provider.Equals("Mock", StringComparison.OrdinalIgnoreCase) &&
            token.StartsWith("mock:admin:", StringComparison.OrdinalIgnoreCase))
        {
            roles.Add("Admin");
        }

        // Generate access token
        var accessToken = _tokenService.GenerateAccessToken(result.UserId!.Value, result.ProviderName!, roles);
        var expiresInSeconds = (int)_tokenService.AccessTokenExpiration.TotalSeconds;

        // Generate refresh token via grain
        var refreshTokenGrain = _clusterClient.GetGrain<IRefreshTokenGrain>(result.UserId!.Value);
        var refreshTokenInfo = await refreshTokenGrain.CreateTokenAsync(result.ProviderName!, roles);

        _logger.LogInformation(
            "Login successful. UserId: {UserId}, Provider: {Provider}, ExternalId: {ExternalId}",
            result.UserId, result.ProviderName, result.ExternalId);

        return new LoginResult(
            Success: true,
            UserId: result.UserId,
            Provider: result.ProviderName,
            Identity: identity,
            AccessToken: accessToken,
            RefreshToken: refreshTokenInfo.TokenId,
            AccessTokenExpiresInSeconds: expiresInSeconds,
            ErrorMessage: null);
    }

    /// <summary>
    /// Refresh access token using a valid refresh token.
    /// Called over existing WebSocket connection - no reconnection needed.
    /// Returns new access + refresh tokens (rotation).
    /// Security: Relies on the unguessability of the refresh token. 
    /// The (UserId, RefreshToken) pair acts as the composite credential.
    /// </summary>
    /// <param name="refreshToken">The refresh token ID from a previous login or refresh.</param>
    /// <param name="userId">The user ID associated with the refresh token.</param>
    public async Task<RefreshResult> RefreshToken(string refreshToken, Guid userId)
    {
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
    /// Get available authentication providers.
    /// </summary>
    public Task<IEnumerable<string>> GetProviders()
    {
        return Task.FromResult(_authServiceFactory.GetProviderNames());
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

/// <summary>
/// Result of a login attempt.
/// </summary>
public record LoginResult(
    bool Success,
    Guid? UserId,
    string? Provider,
    UserIdentity? Identity,
    string? AccessToken,
    string? RefreshToken,
    int? AccessTokenExpiresInSeconds,
    string? ErrorMessage)
{
    public static LoginResult Failed(string? errorMessage) => new(
        Success: false,
        UserId: null,
        Provider: null,
        Identity: null,
        AccessToken: null,
        RefreshToken: null,
        AccessTokenExpiresInSeconds: null,
        ErrorMessage: errorMessage);
}


