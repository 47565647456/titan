using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services.Auth;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for authentication operations.
/// Replaces AuthController with bidirectional communication.
/// </summary>
public class AuthHub : Hub
{
    private readonly IAuthService _authService;
    private readonly IClusterClient _clusterClient;

    public AuthHub(IAuthService authService, IClusterClient clusterClient)
    {
        _authService = authService;
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Authenticate with a token. Returns user info if valid.
    /// For Mock auth, use format: "mock:{guid}"
    /// </summary>
    public async Task<LoginResult> Login(string token)
    {
        var result = await _authService.ValidateTokenAsync(token);
        
        if (!result.Success)
        {
            return new LoginResult(false, null, null, null, result.ErrorMessage);
        }

        // Ensure user identity grain exists and link provider
        var identityGrain = _clusterClient.GetGrain<IUserIdentityGrain>(result.UserId!.Value);
        await identityGrain.LinkProviderAsync(result.ProviderName!, result.ExternalId!);
        var identity = await identityGrain.GetIdentityAsync();

        return new LoginResult(true, result.UserId, result.ProviderName, identity, null);
    }

    /// <summary>
    /// Get user profile.
    /// </summary>
    public async Task<UserProfile> GetProfile(Guid userId)
    {
        var grain = _clusterClient.GetGrain<IUserProfileGrain>(userId);
        return await grain.GetProfileAsync();
    }
}

public record LoginResult(
    bool Success,
    Guid? UserId,
    string? Provider,
    UserIdentity? Identity,
    string? ErrorMessage
);
