using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Contracts;
using Titan.API.Data;
using Titan.API.Services.Auth;

namespace Titan.API.Controllers;

/// <summary>
/// Admin authentication endpoints for the dashboard.
/// Uses ASP.NET Core Identity for user management and session tickets for auth.
/// Implements httpOnly cookies for security.
/// </summary>
[ApiController]
[Route("api/admin/auth")]
[Tags("Admin Authentication")]
public class AdminAuthController : ControllerBase
{
    private readonly UserManager<AdminUser> _userManager;
    private readonly SignInManager<AdminUser> _signInManager;
    private readonly ISessionService _sessionService;
    private readonly ILogger<AdminAuthController> _logger;
    private readonly IValidator<AdminLoginRequest> _loginValidator;

    // Cookie name for httpOnly auth
    private const string SessionCookie = "admin_session";
    private readonly IHostEnvironment _environment;

    public AdminAuthController(
        UserManager<AdminUser> userManager,
        SignInManager<AdminUser> signInManager,
        ISessionService sessionService,
        ILogger<AdminAuthController> logger,
        IValidator<AdminLoginRequest> loginValidator,
        IHostEnvironment environment)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _sessionService = sessionService;
        _logger = logger;
        _loginValidator = loginValidator;
        _environment = environment;
    }

    /// <summary>
    /// Login with email and password.
    /// Sets httpOnly cookie for session ticket.
    /// Also returns session in response body for backward compatibility.
    /// </summary>
    /// <returns>Admin login response with session info.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AdminLoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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

        // Create session with admin flag for shorter lifetime
        var session = await _sessionService.CreateSessionAsync(user.Id, "Dashboard", roles.ToList(), isAdmin: true);

        // Set httpOnly cookie
        SetSessionCookie(session.TicketId, session.ExpiresAt);

        _logger.LogInformation("Admin {Email} logged in successfully", request.Email);

        return Ok(new AdminLoginResponse(
            Success: true,
            UserId: user.Id,
            Email: user.Email!,
            DisplayName: user.DisplayName,
            Roles: roles.ToList(),
            SessionId: session.TicketId,
            ExpiresAt: session.ExpiresAt));
    }

    /// <summary>
    /// Get current authenticated admin user info.
    /// </summary>
    /// <returns>Current admin user information.</returns>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<AdminUserInfo>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    /// Logout: invalidates session and clears auth cookie.
    /// </summary>
    /// <returns>Success status.</returns>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        var sessionId = User.FindFirstValue("session_id");

        bool invalidated = false;
        try
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                invalidated = await _sessionService.InvalidateSessionAsync(sessionId);
                _logger.LogInformation("Admin session invalidated: {Invalidated}", invalidated);
            }
        }
        finally
        {
            ClearSessionCookie();
        }
        
        return Ok(new LogoutResponse(true, invalidated));
    }

    /// <summary>
    /// Revoke all sessions for the current user (security event).
    /// </summary>
    /// <returns>Success status.</returns>
    [HttpPost("revoke-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeAllSessions()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var id))
        {
            return Unauthorized();
        }

        int count = 0;
        try
        {
            count = await _sessionService.InvalidateAllSessionsAsync(id);
            _logger.LogInformation("Admin {UserId} revoked all {Count} sessions", id, count);
        }
        finally
        {
            ClearSessionCookie();
        }

        return Ok(new { success = true, sessionsRevoked = count });
    }

    #region Cookie Helpers

    private void SetSessionCookie(string sessionId, DateTimeOffset expiresAt)
    {
        var isProduction = !_environment.IsDevelopment();

        Response.Cookies.Append(SessionCookie, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expiresAt
        });
    }

    private void ClearSessionCookie()
    {
        var isProduction = !_environment.IsDevelopment();

        Response.Cookies.Delete(SessionCookie, new CookieOptions
        {
            Path = "/",
            Secure = isProduction,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict
        });
    }
    #endregion
}

// Request/Response DTOs

public record AdminLoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
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
