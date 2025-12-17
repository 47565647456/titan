using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Orleans;
using Titan.Abstractions.Grains;
using Titan.API.Data;
using Titan.API.Services.Auth;

namespace Titan.API.Controllers;

/// <summary>
/// Admin authentication endpoints for the dashboard.
/// Uses ASP.NET Core Identity for user management and JWT for stateless auth.
/// Implements httpOnly cookies for security with token rotation.
/// </summary>
[ApiController]
[Route("api/admin/auth")]
[Tags("Admin Authentication")]
public class AdminAuthController : ControllerBase
{
    private readonly UserManager<AdminUser> _userManager;
    private readonly SignInManager<AdminUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<AdminAuthController> _logger;
    private readonly IValidator<AdminLoginRequest> _loginValidator;

    // Cookie names for httpOnly auth
    private const string AccessTokenCookie = "admin_access_token";
    private const string RefreshTokenCookie = "admin_refresh_token";
    private const string UserIdCookie = "admin_user_id";

    // Admin refresh token lifetime (shorter than game client for security)
    private static readonly TimeSpan AdminRefreshTokenLifetime = TimeSpan.FromHours(1);

    public AdminAuthController(
        UserManager<AdminUser> userManager,
        SignInManager<AdminUser> signInManager,
        ITokenService tokenService,
        IClusterClient clusterClient,
        ILogger<AdminAuthController> logger,
        IValidator<AdminLoginRequest> loginValidator)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _clusterClient = clusterClient;
        _logger = logger;
        _loginValidator = loginValidator;
    }

    /// <summary>
    /// Login with email and password.
    /// Sets httpOnly cookies for access and refresh tokens.
    /// Also returns tokens in response body for backward compatibility.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AdminLoginResponse>> Login([FromBody] AdminLoginRequest request)
    {
        var validationResult = await _loginValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            _logger.LogWarning("Login failed: user {Email} not found", request.Email);
            return Unauthorized(new { error = "Invalid email or password" });
        }


        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Login failed: user {Email} is locked out", request.Email);
            return Unauthorized(new { error = "Account locked out. Please try again later." });
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Login failed: invalid password for {Email}", request.Email);
            return Unauthorized(new { error = "Invalid email or password" });
        }

        // Update last login timestamp
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _userManager.UpdateAsync(user);

        // Get user roles
        var roles = await _userManager.GetRolesAsync(user);

        // Generate JWT access token
        var accessToken = _tokenService.GenerateAccessToken(user.Id, "Dashboard", roles.ToList());
        var accessTokenExpiry = _tokenService.AccessTokenExpiration;

        // Create persistent refresh token via grain (with admin-specific shorter lifetime)
        var refreshTokenGrain = _clusterClient.GetGrain<IRefreshTokenGrain>(user.Id);
        var refreshTokenInfo = await refreshTokenGrain.CreateTokenAsync("Dashboard", roles.ToList());

        // Set httpOnly cookies
        SetAuthCookies(accessToken, refreshTokenInfo.TokenId, user.Id, accessTokenExpiry);

        _logger.LogInformation("Admin {Email} logged in successfully", request.Email);

        return Ok(new AdminLoginResponse
        {
            Success = true,
            UserId = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            Roles = roles.ToList(),
            AccessToken = accessToken,
            RefreshToken = refreshTokenInfo.TokenId,
            ExpiresInSeconds = (int)accessTokenExpiry.TotalSeconds
        });
    }

    /// <summary>
    /// Refresh access token using the refresh token from httpOnly cookie.
    /// Returns new access and refresh tokens (token rotation).
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AdminRefreshResponse>> Refresh()
    {
        // Get refresh token and user ID from httpOnly cookies
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie, out var refreshToken) ||
            !Request.Cookies.TryGetValue(UserIdCookie, out var userIdStr) ||
            !Guid.TryParse(userIdStr, out var userId))
        {
            _logger.LogWarning("Refresh failed: missing or invalid cookies");
            ClearAuthCookies();
            return Unauthorized(new { error = "No valid session" });
        }

        // Validate and consume the refresh token
        var refreshTokenGrain = _clusterClient.GetGrain<IRefreshTokenGrain>(userId);
        var tokenInfo = await refreshTokenGrain.ConsumeTokenAsync(refreshToken);

        if (tokenInfo == null)
        {
            _logger.LogWarning("Refresh failed: invalid or expired refresh token for user {UserId}", userId);
            ClearAuthCookies();
            return Unauthorized(new { error = "Invalid or expired session" });
        }

        // Verify user still exists and is not locked out
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null || await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("Refresh failed: user {UserId} not found or locked out", userId);
            ClearAuthCookies();
            await refreshTokenGrain.RevokeAllTokensAsync();
            return Unauthorized(new { error = "Account unavailable" });
        }

        // Get current roles (may have changed since last login)
        var roles = await _userManager.GetRolesAsync(user);

        // Generate new access token
        var accessToken = _tokenService.GenerateAccessToken(userId, tokenInfo.Provider, roles.ToList());
        var accessTokenExpiry = _tokenService.AccessTokenExpiration;

        // Generate new refresh token (rotation)
        var newRefreshTokenInfo = await refreshTokenGrain.CreateTokenAsync(tokenInfo.Provider, roles.ToList());

        // Set new httpOnly cookies
        SetAuthCookies(accessToken, newRefreshTokenInfo.TokenId, userId, accessTokenExpiry);

        _logger.LogDebug("Token refreshed for admin user {UserId}", userId);

        return Ok(new AdminRefreshResponse
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = newRefreshTokenInfo.TokenId,
            ExpiresInSeconds = (int)accessTokenExpiry.TotalSeconds
        });
    }

    /// <summary>
    /// Get current authenticated admin user info.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AdminUserInfo>> GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var id))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new AdminUserInfo
        {
            UserId = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            Roles = roles.ToList(),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// Logout: revokes refresh token and clears auth cookies.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Revoke the refresh token if we have it
        if (Request.Cookies.TryGetValue(RefreshTokenCookie, out var refreshToken) &&
            !string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var id))
        {
            var refreshTokenGrain = _clusterClient.GetGrain<IRefreshTokenGrain>(id);
            await refreshTokenGrain.RevokeTokenAsync(refreshToken);
            _logger.LogInformation("Admin {UserId} logged out, refresh token revoked", id);
        }

        // Clear auth cookies
        ClearAuthCookies();

        return Ok(new { success = true });
    }

    /// <summary>
    /// Revoke all refresh tokens for the current user (security event).
    /// </summary>
    [HttpPost("revoke-all")]
    [Authorize]
    public async Task<IActionResult> RevokeAllTokens()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var id))
        {
            return Unauthorized();
        }

        var refreshTokenGrain = _clusterClient.GetGrain<IRefreshTokenGrain>(id);
        await refreshTokenGrain.RevokeAllTokensAsync();

        ClearAuthCookies();

        _logger.LogInformation("Admin {UserId} revoked all refresh tokens", id);

        return Ok(new { success = true });
    }

    #region Cookie Helpers

    private void SetAuthCookies(string accessToken, string refreshToken, Guid userId, TimeSpan accessTokenExpiry)
    {
        var isProduction = !string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);

        // Access token cookie - expires with the token
        Response.Cookies.Append(AccessTokenCookie, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.Add(accessTokenExpiry)
        });

        // Refresh token cookie - longer expiry
        Response.Cookies.Append(RefreshTokenCookie, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Path = "/api/admin/auth",
            Expires = DateTimeOffset.UtcNow.Add(AdminRefreshTokenLifetime)
        });

        // User ID cookie - needed for refresh (not secret, but still httpOnly)
        Response.Cookies.Append(UserIdCookie, userId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Path = "/api/admin/auth",
            Expires = DateTimeOffset.UtcNow.Add(AdminRefreshTokenLifetime)
        });
    }

    private void ClearAuthCookies()
    {
        var isProduction = !string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);

        // Options must match the path and secure flag of the original cookie
        var rootOptions = new CookieOptions { 
            Path = "/", 
            Secure = isProduction, 
            HttpOnly = true 
        };

        var authOptions = new CookieOptions 
        { 
            Path = "/api/admin/auth", 
            Secure = isProduction, 
            HttpOnly = true 
        };

        Response.Cookies.Delete(AccessTokenCookie, rootOptions);
        Response.Cookies.Delete(RefreshTokenCookie, authOptions);
        Response.Cookies.Delete(UserIdCookie, authOptions);
    }
    #endregion
}

// Request/Response DTOs

public record AdminLoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public record AdminLoginResponse
{
    public bool Success { get; init; }
    public Guid UserId { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public required List<string> Roles { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public int ExpiresInSeconds { get; init; }
}

public record AdminRefreshResponse
{
    public bool Success { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public int ExpiresInSeconds { get; init; }
}

public record AdminUserInfo
{
    public Guid UserId { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public required List<string> Roles { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}
