using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models;

public enum TradeStatus
{
    Pending,
    Accepted,
    Rejected,
    Cancelled,
    Completed,
    Failed,
    Expired
}

/// <summary>
/// Represents a trade session between two characters within a season.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record TradeSession
{
    [Id(0), MemoryPackOrder(0)] public required Guid TradeId { get; init; }
    [Id(1), MemoryPackOrder(1)] public required Guid InitiatorCharacterId { get; init; }
    [Id(2), MemoryPackOrder(2)] public required Guid TargetCharacterId { get; init; }
    [Id(3), MemoryPackOrder(3)] public required string SeasonId { get; init; }
    [Id(4), MemoryPackOrder(4)] public List<Guid> InitiatorItemIds { get; init; } = new();
    [Id(5), MemoryPackOrder(5)] public List<Guid> TargetItemIds { get; init; } = new();
    [Id(6), MemoryPackOrder(6)] public TradeStatus Status { get; init; } = TradeStatus.Pending;
    [Id(7), MemoryPackOrder(7)] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    [Id(8), MemoryPackOrder(8)] public DateTimeOffset? CompletedAt { get; init; }
    [Id(9), MemoryPackOrder(9)] public bool InitiatorAccepted { get; init; }
    [Id(10), MemoryPackOrder(10)] public bool TargetAccepted { get; init; }
}
