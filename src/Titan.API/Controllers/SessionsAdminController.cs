using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.API.Services.Auth;

namespace Titan.API.Controllers;

/// <summary>
/// Admin session management endpoints for the dashboard.
/// Requires SuperAdmin role for all operations.
/// </summary>
[ApiController]
[Route("api/admin/sessions")]
[Tags("Sessions")]
[Authorize(Policy = "AdminDashboard")]
public class SessionsAdminController : ControllerBase
{
    private readonly ISessionService _sessionService;
    private readonly ILogger<SessionsAdminController> _logger;

    public SessionsAdminController(
        ISessionService sessionService,
        ILogger<SessionsAdminController> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <summary>
    /// Get all active sessions with pagination.
    /// </summary>
    /// <param name="skip">Number of sessions to skip (default: 0).</param>
    /// <param name="take">Number of sessions to return (default: 50, max: 100).</param>
    /// <returns>Paginated list of active sessions.</returns>
    [HttpGet]
    public async Task<ActionResult<SessionListDto>> GetSessions(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        if (skip < 0) skip = 0;
        if (take < 1) take = 1;
        if (take > 100) take = 100;

        var result = await _sessionService.GetAllSessionsAsync(skip, take);
        
        _logger.LogDebug("Retrieved {Count} sessions (skip: {Skip}, take: {Take}, total: {Total})",
            result.Sessions.Count, skip, take, result.TotalCount);

        return Ok(new SessionListDto(
            Sessions: result.Sessions.Select(ToDto).ToList(),
            TotalCount: result.TotalCount,
            Skip: result.Skip,
            Take: result.Take
        ));
    }

    /// <summary>
    /// Get total count of active sessions.
    /// </summary>
    /// <returns>Session count.</returns>
    [HttpGet("count")]
    public async Task<ActionResult<SessionCountDto>> GetSessionCount()
    {
        var count = await _sessionService.GetSessionCountAsync();
        return Ok(new SessionCountDto(count));
    }

    /// <summary>
    /// Get all sessions for a specific user.
    /// </summary>
    /// <param name="userId">The user ID to get sessions for.</param>
    /// <returns>List of user's sessions.</returns>
    [HttpGet("user/{userId:guid}")]
    public async Task<ActionResult<List<SessionInfoDto>>> GetUserSessions(Guid userId)
    {
        var sessions = await _sessionService.GetUserSessionsAsync(userId);
        
        _logger.LogDebug("Retrieved {Count} sessions for user {UserId}",
            sessions.Count, userId);

        return Ok(sessions.Select(ToDto).ToList());
    }

    /// <summary>
    /// Invalidate a specific session by ticket ID.
    /// </summary>
    /// <param name="ticketId">The session ticket ID to invalidate.</param>
    /// <returns>Success status.</returns>
    [HttpDelete("{ticketId}")]
    public async Task<ActionResult<InvalidateResultDto>> InvalidateSession(string ticketId)
    {
        var success = await _sessionService.InvalidateSessionAsync(ticketId);
        
        if (success)
        {
            _logger.LogInformation("Admin invalidated session {TicketId}", ticketId[..Math.Min(8, ticketId.Length)]);
        }

        return Ok(new InvalidateResultDto(success));
    }

    /// <summary>
    /// Invalidate all sessions for a specific user.
    /// </summary>
    /// <param name="userId">The user ID to invalidate sessions for.</param>
    /// <returns>Number of sessions invalidated.</returns>
    [HttpDelete("user/{userId:guid}")]
    public async Task<ActionResult<InvalidateAllResultDto>> InvalidateUserSessions(Guid userId)
    {
        var count = await _sessionService.InvalidateAllSessionsAsync(userId);
        
        _logger.LogInformation("Admin invalidated {Count} sessions for user {UserId}", count, userId);

        return Ok(new InvalidateAllResultDto(count));
    }

    private static SessionInfoDto ToDto(SessionInfo session) => new(
        TicketId: session.TicketId,
        UserId: session.UserId.ToString(),
        Provider: session.Provider,
        Roles: session.Roles.ToList(),
        CreatedAt: session.CreatedAt,
        ExpiresAt: session.ExpiresAt,
        LastActivityAt: session.LastActivityAt,
        IsAdmin: session.IsAdmin
    );
}

// DTOs

public record SessionInfoDto(
    string TicketId,
    string UserId,
    string Provider,
    List<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset LastActivityAt,
    bool IsAdmin);

public record SessionListDto(
    List<SessionInfoDto> Sessions,
    int TotalCount,
    int Skip,
    int Take);

public record SessionCountDto(int Count);

public record InvalidateResultDto(bool Success);

public record InvalidateAllResultDto(int Count);
