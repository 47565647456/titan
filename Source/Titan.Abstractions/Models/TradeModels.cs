using Orleans;

namespace Titan.Abstractions.Models;

public enum TradeStatus
{
    Pending,
    Accepted,
    Rejected,
    Cancelled,
    Completed,
    Failed
}

/// <summary>
/// Represents a trade session between two players.
/// </summary>
[GenerateSerializer]
public record TradeSession
{
    [Id(0)] public required Guid TradeId { get; init; }
    [Id(1)] public required Guid InitiatorUserId { get; init; }
    [Id(2)] public required Guid TargetUserId { get; init; }
    [Id(3)] public List<Guid> InitiatorItemIds { get; init; } = new();
    [Id(4)] public List<Guid> TargetItemIds { get; init; } = new();
    [Id(5)] public TradeStatus Status { get; init; } = TradeStatus.Pending;
    [Id(6)] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    [Id(7)] public DateTimeOffset? CompletedAt { get; init; }
    [Id(8)] public bool InitiatorAccepted { get; init; }
    [Id(9)] public bool TargetAccepted { get; init; }
}
