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
    /// Authenticate with a provider token. Returns user info and JWT if valid.
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
            return new LoginResult(false, null, null, null, null, 
                $"Unknown provider '{provider}'. Available: {available}");
        }

        var authService = _authServiceFactory.GetService(provider);
        var result = await authService.ValidateTokenAsync(token);
        
        if (!result.Success)
        {
            _logger.LogWarning("Login failed for provider {Provider}: {Error}", 
                provider, result.ErrorMessage);
            return new LoginResult(false, null, null, null, null, result.ErrorMessage);
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

        // Generate JWT for subsequent authenticated requests
        var jwt = _tokenService.GenerateToken(result.UserId!.Value, result.ProviderName!, roles);

        _logger.LogInformation(
            "Login successful. UserId: {UserId}, Provider: {Provider}, ExternalId: {ExternalId}",
            result.UserId, result.ProviderName, result.ExternalId);

        return new LoginResult(true, result.UserId, result.ProviderName, identity, jwt, null);
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
    string? Token,
    string? ErrorMessage
);
