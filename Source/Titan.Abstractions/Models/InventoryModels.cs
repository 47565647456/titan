using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Represents an item in a player's inventory.
/// </summary>
[GenerateSerializer]
public record Item
{
    [Id(0)] public required Guid Id { get; init; }
    [Id(1)] public required string ItemTypeId { get; init; }
    [Id(2)] public int Quantity { get; init; } = 1;
    [Id(3)] public Dictionary<string, object>? Metadata { get; init; }
    [Id(4)] public DateTimeOffset AcquiredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A historical record of an item event.
/// </summary>
[GenerateSerializer]
public record ItemHistoryEntry
{
    [Id(0)] public required DateTimeOffset Timestamp { get; init; }
    [Id(1)] public required string EventType { get; init; }
    [Id(2)] public required Guid ActorUserId { get; init; }
    [Id(3)] public Guid? TargetUserId { get; init; }
    [Id(4)] public string? Details { get; init; }
}
