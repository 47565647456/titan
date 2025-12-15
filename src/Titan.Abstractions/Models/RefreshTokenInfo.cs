using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Information about a refresh token stored in grain state.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record RefreshTokenInfo
{
    [Id(0), MemoryPackOrder(0)] public required string TokenId { get; init; }
    [Id(1), MemoryPackOrder(1)] public required string Provider { get; init; }
    [Id(2), MemoryPackOrder(2)] public required IReadOnlyList<string> Roles { get; init; }
    [Id(3), MemoryPackOrder(3)] public required DateTimeOffset CreatedAt { get; init; }
    [Id(4), MemoryPackOrder(4)] public required DateTimeOffset ExpiresAt { get; init; }
}
