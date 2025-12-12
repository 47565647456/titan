using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Type of season - permanent leagues never reset, temporary seasons have defined lifecycles.
/// </summary>
public enum SeasonType
{
    Permanent,
    Temporary
}

/// <summary>
/// Current status of a season.
/// </summary>
public enum SeasonStatus
{
    Upcoming,
    Active,
    Ended,
    Migrating,
    Archived
}

/// <summary>
/// Represents a season/league in the game.
/// Seasons are time-bounded periods where players can create characters.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record Season
{
    /// <summary>
    /// Unique identifier for the season (e.g., "standard", "s1", "s2").
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required string SeasonId { get; init; }

    /// <summary>
    /// Display name for the season (e.g., "Season 1: The Awakening").
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required string Name { get; init; }

    /// <summary>
    /// Whether this is a permanent or temporary season.
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public SeasonType Type { get; init; }

    /// <summary>
    /// When the season starts.
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public DateTimeOffset StartDate { get; init; }

    /// <summary>
    /// When the season ends (null for permanent seasons).
    /// </summary>
    [Id(4), MemoryPackOrder(4)] public DateTimeOffset? EndDate { get; init; }

    /// <summary>
    /// Current status of the season.
    /// </summary>
    [Id(5), MemoryPackOrder(5)] public SeasonStatus Status { get; init; }

    /// <summary>
    /// The season to migrate characters to when this season ends.
    /// Typically "standard" for all temporary seasons.
    /// </summary>
    [Id(6), MemoryPackOrder(6)] public string MigrationTargetId { get; init; } = "standard";

    /// <summary>
    /// Optional season-specific modifiers as JSON-serialized key-value pairs.
    /// </summary>
    [Id(7), MemoryPackOrder(7)] public Dictionary<string, string>? Modifiers { get; init; }

    /// <summary>
    /// Whether this is a Void League. Void league characters and items do NOT migrate
    /// to Standard when the season ends or on Hardcore death.
    /// </summary>
    [Id(8), MemoryPackOrder(8)] public bool IsVoid { get; init; }
}

/// <summary>
/// A challenge available during a specific season.
/// Completing challenges can unlock cosmetic rewards.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record SeasonChallenge
{
    [Id(0), MemoryPackOrder(0)] public required string ChallengeId { get; init; }
    [Id(1), MemoryPackOrder(1)] public required string SeasonId { get; init; }
    [Id(2), MemoryPackOrder(2)] public required string Name { get; init; }
    [Id(3), MemoryPackOrder(3)] public string? Description { get; init; }
    [Id(4), MemoryPackOrder(4)] public int RequiredProgress { get; init; }
    [Id(5), MemoryPackOrder(5)] public string? RewardCosmeticId { get; init; }
}
