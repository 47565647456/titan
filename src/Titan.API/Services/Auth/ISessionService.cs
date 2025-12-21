namespace Titan.API.Services.Auth;

/// <summary>
/// Result of session creation.
/// </summary>
public record SessionCreateResult(string TicketId, DateTimeOffset ExpiresAt);

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
}
