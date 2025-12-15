using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Represents a player's current online presence status.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record PlayerPresence
{
    [Id(0), MemoryPackOrder(0)] public required Guid UserId { get; init; }
    [Id(1), MemoryPackOrder(1)] public bool IsOnline { get; init; }
    [Id(2), MemoryPackOrder(2)] public int ConnectionCount { get; init; }
    [Id(3), MemoryPackOrder(3)] public DateTimeOffset LastSeen { get; init; }
    [Id(4), MemoryPackOrder(4)] public string? CurrentActivity { get; init; }
}

/// <summary>
/// Represents an active connection session (in-memory, not persisted).
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record PlayerSession
{
    [Id(0), MemoryPackOrder(0)] public required string ConnectionId { get; init; }
    [Id(1), MemoryPackOrder(1)] public required Guid UserId { get; init; }
    [Id(2), MemoryPackOrder(2)] public DateTimeOffset ConnectedAt { get; init; }
    [Id(3), MemoryPackOrder(3)] public string? HubName { get; init; }
}

/// <summary>
/// Persisted session log entry (stored in DB).
/// Tracks login/logout history for analytics and security auditing.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record SessionLog
{
    [Id(0), MemoryPackOrder(0)] public required Guid SessionId { get; init; }
    [Id(1), MemoryPackOrder(1)] public required Guid UserId { get; init; }
    [Id(2), MemoryPackOrder(2)] public DateTimeOffset LoginAt { get; init; }
    [Id(3), MemoryPackOrder(3)] public DateTimeOffset? LogoutAt { get; init; }
    [Id(4), MemoryPackOrder(4)] public TimeSpan? Duration { get; init; }
    [Id(5), MemoryPackOrder(5)] public string? IpAddress { get; init; }
}
