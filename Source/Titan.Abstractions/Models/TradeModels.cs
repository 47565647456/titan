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
public record TradeSession
{
    [Id(0)] public required Guid TradeId { get; init; }
    [Id(1)] public required Guid InitiatorCharacterId { get; init; }
    [Id(2)] public required Guid TargetCharacterId { get; init; }
    [Id(3)] public required string SeasonId { get; init; }
    [Id(4)] public List<Guid> InitiatorItemIds { get; init; } = new();
    [Id(5)] public List<Guid> TargetItemIds { get; init; } = new();
    [Id(6)] public TradeStatus Status { get; init; } = TradeStatus.Pending;
    [Id(7)] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    [Id(8)] public DateTimeOffset? CompletedAt { get; init; }
    [Id(9)] public bool InitiatorAccepted { get; init; }
    [Id(10)] public bool TargetAccepted { get; init; }
}
