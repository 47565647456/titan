using Orleans;

namespace Titan.Abstractions.Events;

/// <summary>
/// Event published when season state changes occur.
/// </summary>
[GenerateSerializer]
public record SeasonEvent
{
    [Id(0)] public required string SeasonId { get; init; }
    [Id(1)] public required string EventType { get; init; }
    [Id(2)] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    [Id(3)] public object? Data { get; init; }
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
