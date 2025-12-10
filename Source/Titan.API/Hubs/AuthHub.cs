using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services.Auth;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for authentication operations.
/// This hub is intentionally NOT protected by [Authorize] to allow login.
/// </summary>
public class AuthHub : Hub
{
    private readonly IAuthService _authService;
    private readonly IClusterClient _clusterClient;
    private readonly ITokenService _tokenService;

    public AuthHub(IAuthService authService, IClusterClient clusterClient, ITokenService tokenService)
    {
        _authService = authService;
        _clusterClient = clusterClient;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Authenticate with a provider token. Returns user info and JWT if valid.
    /// For Mock auth, use format: "mock:{guid}" for regular user, "mock:admin:{guid}" for admin.
    /// </summary>
    public async Task<LoginResult> Login(string token)
    {
        var result = await _authService.ValidateTokenAsync(token);
        
        if (!result.Success)
        {
            return new LoginResult(false, null, null, null, null, result.ErrorMessage);
        }

        // Ensure user identity grain exists and link provider
        var identityGrain = _clusterClient.GetGrain<IUserIdentityGrain>(result.UserId!.Value);
        await identityGrain.LinkProviderAsync(result.ProviderName!, result.ExternalId!);
        var identity = await identityGrain.GetIdentityAsync();

        // Determine roles
        var roles = new List<string> { "User" };
        
        // Admin backdoor for development: "mock:admin:{guid}"
        if (token.StartsWith("mock:admin:", StringComparison.OrdinalIgnoreCase))
        {
            roles.Add("Admin");
        }

        // Generate JWT for subsequent authenticated requests
        var jwt = _tokenService.GenerateToken(result.UserId!.Value, result.ProviderName!, roles);

        return new LoginResult(true, result.UserId, result.ProviderName, identity, jwt, null);
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
    string? Token,
    string? ErrorMessage
);

