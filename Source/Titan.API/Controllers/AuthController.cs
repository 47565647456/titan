using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains;
using Titan.API.Services.Auth;

namespace Titan.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IClusterClient _clusterClient;

    public AuthController(IAuthService authService, IClusterClient clusterClient)
    {
        _authService = authService;
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Authenticate with a token. Returns user info if valid.
    /// For Mock auth, use format: "mock:{guid}"
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.ValidateTokenAsync(request.Token);
        
        if (!result.Success)
            return Unauthorized(new { Error = result.ErrorMessage });

        // Ensure user identity grain exists and link provider
        var identityGrain = _clusterClient.GetGrain<IUserIdentityGrain>(result.UserId!.Value);
        await identityGrain.LinkProviderAsync(result.ProviderName!, result.ExternalId!);
        var identity = await identityGrain.GetIdentityAsync();

        return Ok(new
        {
            UserId = result.UserId,
            Provider = result.ProviderName,
            Identity = identity
        });
    }

    /// <summary>
    /// Get user profile.
    /// </summary>
    [HttpGet("profile/{userId:guid}")]
    public async Task<IActionResult> GetProfile(Guid userId)
    {
        var profileGrain = _clusterClient.GetGrain<IUserProfileGrain>(userId);
        var profile = await profileGrain.GetProfileAsync();
        return Ok(profile);
    }
}

public record LoginRequest(string Token);
