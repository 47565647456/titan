using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Events;

/// <summary>
/// Event published when season state changes occur.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record SeasonEvent
{
    [Id(0), MemoryPackOrder(0)] public required string SeasonId { get; init; }
    [Id(1), MemoryPackOrder(1)] public required string EventType { get; init; }
    [Id(2), MemoryPackOrder(2)] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Optional event-specific data as a JSON-serialized string.
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public string? Data { get; init; }
}

/// <summary>
/// Event types for season-related events.
/// </summary>
public static class SeasonEventTypes
{
    public const string SeasonCreated = "SeasonCreated";
    public const string SeasonStarted = "SeasonStarted";
    public const string SeasonEnded = "SeasonEnded";
    public const string MigrationStarted = "MigrationStarted";
    public const string MigrationProgress = "MigrationProgress";
    public const string MigrationCompleted = "MigrationCompleted";
    public const string CharacterCreated = "CharacterCreated";
    public const string CharacterDied = "CharacterDied";
    public const string CharacterMigrated = "CharacterMigrated";
}
