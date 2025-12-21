using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services.Auth;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for authentication operations.
/// Login is handled via HTTP (POST /api/auth/login) following industry standards.
/// This hub provides session management utilities.
/// </summary>
public class AuthHub : Hub
{
    private readonly IClusterClient _clusterClient;
    private readonly ISessionService _sessionService;
    private readonly ILogger<AuthHub> _logger;

    public AuthHub(
        IClusterClient clusterClient, 
        ISessionService sessionService,
        ILogger<AuthHub> logger)
    {
        _clusterClient = clusterClient;
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <summary>
    /// Logout current session.
    /// </summary>
    [Authorize]
    public async Task Logout()
    {
        var userId = Guid.Parse(Context.UserIdentifier!);
        var sessionId = Context.User?.FindFirstValue("session_id");
        
        if (!string.IsNullOrEmpty(sessionId))
        {
            await _sessionService.InvalidateSessionAsync(sessionId);
            _logger.LogInformation("User {UserId} logged out via hub, session invalidated", userId);
        }
    }

    /// <summary>
    /// Revoke all sessions for the current user (e.g., security event).
    /// </summary>
    [Authorize]
    public async Task<int> RevokeAllSessions()
    {
        var userId = Guid.Parse(Context.UserIdentifier!);
        var count = await _sessionService.InvalidateAllSessionsAsync(userId);
        
        _logger.LogInformation("User {UserId} revoked all {Count} sessions", userId, count);
        return count;
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
