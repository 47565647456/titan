namespace Titan.API.Services.Auth;

/// <summary>
/// Result of session creation.
/// </summary>
public record SessionCreateResult(string TicketId, DateTimeOffset ExpiresAt);

/// <summary>
/// Session information for admin dashboard display.
/// </summary>
public record SessionInfo(
    string TicketId,
    Guid UserId,
    string Provider,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset LastActivityAt,
    bool IsAdmin);

/// <summary>
/// Paginated session list result.
/// </summary>
public record SessionListResult(
    IReadOnlyList<SessionInfo> Sessions,
    int TotalCount,
    int Skip,
    int Take);

/// <summary>
/// Session management service.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a new session for the user. Enforces max sessions limit.
    /// </summary>
    Task<SessionCreateResult> CreateSessionAsync(
        Guid userId, 
        string provider, 
        IEnumerable<string> roles,
        bool isAdmin = false);

    /// <summary>
    /// Validates a session ticket and returns session data.
    /// Applies sliding expiration on successful validation.
    /// </summary>
    Task<SessionTicket?> ValidateSessionAsync(string ticketId);

    /// <summary>
    /// Invalidates a single session.
    /// </summary>
    Task<bool> InvalidateSessionAsync(string ticketId);

    /// <summary>
    /// Invalidates all sessions for a user.
    /// </summary>
    Task<int> InvalidateAllSessionsAsync(Guid userId);

    /// <summary>
    /// Gets all active sessions with pagination.
    /// </summary>
    Task<SessionListResult> GetAllSessionsAsync(int skip = 0, int take = 50);

    /// <summary>
    /// Gets the total count of active sessions.
    /// </summary>
    Task<int> GetSessionCountAsync();

    /// <summary>
    /// Gets all sessions for a specific user.
    /// </summary>
    Task<IReadOnlyList<SessionInfo>> GetUserSessionsAsync(Guid userId);
}
