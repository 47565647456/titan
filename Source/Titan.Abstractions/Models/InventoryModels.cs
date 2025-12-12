using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Represents an item in a player's inventory.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record Item
{
    [Id(0), MemoryPackOrder(0)] public required Guid Id { get; init; }
    [Id(1), MemoryPackOrder(1)] public required string ItemTypeId { get; init; }
    [Id(2), MemoryPackOrder(2)] public int Quantity { get; init; } = 1;
    /// <summary>
    /// Optional metadata as JSON-serialized key-value pairs for extensibility.
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public Dictionary<string, string>? Metadata { get; init; }
    [Id(4), MemoryPackOrder(4)] public DateTimeOffset AcquiredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A historical record of an item event.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record ItemHistoryEntry
{
    [Id(0), MemoryPackOrder(0)] public required DateTimeOffset Timestamp { get; init; }
    [Id(1), MemoryPackOrder(1)] public required string EventType { get; init; }
    [Id(2), MemoryPackOrder(2)] public required Guid ActorUserId { get; init; }
    [Id(3), MemoryPackOrder(3)] public Guid? TargetUserId { get; init; }
    [Id(4), MemoryPackOrder(4)] public string? Details { get; init; }
}
