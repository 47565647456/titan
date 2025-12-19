using MemoryPack;

namespace Titan.Abstractions.Models;

/// <summary>
/// A short-lived, single-use ticket for WebSocket connection authentication.
/// Used to avoid exposing JWTs in query strings which can leak in logs.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record ConnectionTicket
{
    /// <summary>
    /// Unique identifier for this ticket (used as the grain key).
    /// </summary>
    [Id(0), MemoryPackOrder(0)]
    public required string TicketId { get; init; }

    /// <summary>
    /// The user ID this ticket authenticates.
    /// </summary>
    [Id(1), MemoryPackOrder(1)]
    public required Guid UserId { get; init; }

    /// <summary>
    /// The roles associated with this user.
    /// </summary>
    [Id(2), MemoryPackOrder(2)]
    public required string[] Roles { get; init; }

    /// <summary>
    /// When this ticket expires.
    /// </summary>
    [Id(3), MemoryPackOrder(3)]
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Whether this ticket has been consumed.
    /// </summary>
    [Id(4), MemoryPackOrder(4)]
    public bool IsConsumed { get; init; }
}
