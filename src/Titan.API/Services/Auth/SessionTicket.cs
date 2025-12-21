using MemoryPack;

namespace Titan.API.Services.Auth;

/// <summary>
/// Session data stored in Redis.
/// </summary>
[MemoryPackable]
public partial record SessionTicket
{
    [MemoryPackOrder(0)] public required Guid UserId { get; init; }
    [MemoryPackOrder(1)] public required string Provider { get; init; }
    [MemoryPackOrder(2)] public required IReadOnlyList<string> Roles { get; init; }
    [MemoryPackOrder(3)] public required DateTimeOffset CreatedAt { get; init; }
    [MemoryPackOrder(4)] public required DateTimeOffset ExpiresAt { get; init; }
    [MemoryPackOrder(5)] public DateTimeOffset LastActivityAt { get; set; }
    [MemoryPackOrder(6)] public bool IsAdmin { get; init; }
}

