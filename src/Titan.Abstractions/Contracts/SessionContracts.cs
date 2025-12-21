namespace Titan.Abstractions.Contracts;

/// <summary>
/// Session information for admin dashboard display.
/// </summary>
public record SessionInfoDto(
    string TicketId,
    string UserId,
    string Provider,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset LastActivityAt,
    bool IsAdmin);

/// <summary>
/// Paginated session list response.
/// </summary>
public record SessionListDto(
    IReadOnlyList<SessionInfoDto> Sessions,
    int TotalCount,
    int Skip,
    int Take);

/// <summary>
/// Session count response.
/// </summary>
public record SessionCountDto(int Count);

/// <summary>
/// Session invalidation result.
/// </summary>
public record InvalidateSessionResultDto(bool Success);

/// <summary>
/// Bulk session invalidation result.
/// </summary>
public record InvalidateAllSessionsResultDto(int Count);
