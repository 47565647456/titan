using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Player-chosen restrictions at character creation (immutable after creation).
/// Similar to Hades' Heat system - players pick their difficulty modifiers.
/// </summary>
[Flags]
public enum CharacterRestrictions
{
    /// <summary>
    /// No restrictions - standard gameplay.
    /// </summary>
    None = 0,

    /// <summary>
    /// Permadeath - character migrates to permanent season on death.
    /// </summary>
    Hardcore = 1 << 0,

    /// <summary>
    /// Solo Self-Found - no trading, no party play, fully solo experience.
    /// </summary>
    SoloSelfFound = 1 << 1,

    // Future restrictions:
    // Ironman = 1 << 2,      // No stash, inventory only
    // Ruthless = 1 << 3,     // Reduced drop rates
    // GroupSSF = 1 << 4,     // Trade only within pre-defined group
}

/// <summary>
/// Represents a player's character within a specific season.
/// Characters are season-scoped and can have player-chosen restrictions.
/// </summary>
[GenerateSerializer]
public record Character
{
    /// <summary>
    /// Unique character identifier.
    /// </summary>
    [Id(0)] public required Guid CharacterId { get; init; }

    /// <summary>
    /// The account this character belongs to.
    /// </summary>
    [Id(1)] public required Guid AccountId { get; init; }

    /// <summary>
    /// The season this character exists in.
    /// </summary>
    [Id(2)] public required string SeasonId { get; init; }

    /// <summary>
    /// Character display name.
    /// </summary>
    [Id(3)] public required string Name { get; init; }

    /// <summary>
    /// Player-chosen restrictions (Hardcore, SSF, etc.). Immutable after creation.
    /// </summary>
    [Id(4)] public CharacterRestrictions Restrictions { get; init; }

    /// <summary>
    /// Whether this Hardcore character has died (triggers migration).
    /// </summary>
    [Id(5)] public bool IsDead { get; init; }

    /// <summary>
    /// Character level.
    /// </summary>
    [Id(6)] public int Level { get; init; } = 1;

    /// <summary>
    /// Total experience points.
    /// </summary>
    [Id(7)] public long Experience { get; init; }

    /// <summary>
    /// Character stats (strength, dexterity, etc.).
    /// </summary>
    [Id(8)] public Dictionary<string, int> Stats { get; init; } = new();

    /// <summary>
    /// When the character was created.
    /// </summary>
    [Id(9)] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether this character has been migrated from another season.
    /// </summary>
    [Id(10)] public bool IsMigrated { get; init; }

    /// <summary>
    /// The original season if this character was migrated.
    /// </summary>
    [Id(11)] public string? OriginalSeasonId { get; init; }
}

/// <summary>
/// Tracks a character's progress on a season challenge.
/// </summary>
[GenerateSerializer]
public record ChallengeProgress
{
    [Id(0)] public required string ChallengeId { get; init; }
    [Id(1)] public int CurrentProgress { get; init; }
    [Id(2)] public bool IsCompleted { get; init; }
    [Id(3)] public DateTimeOffset? CompletedAt { get; init; }
}
