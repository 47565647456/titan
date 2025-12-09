using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Orchestrates end-of-season migration for all characters in a season.
/// Key: SeasonId (string)
/// </summary>
public interface ISeasonMigrationGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets the current migration status.
    /// </summary>
    Task<MigrationStatus> GetStatusAsync();

    /// <summary>
    /// Starts bulk migration of all characters from this season to the target season.
    /// Called when a season ends.
    /// </summary>
    Task<MigrationStatus> StartMigrationAsync(string targetSeasonId);

    /// <summary>
    /// Migrates a single character to the target season.
    /// Can be called as part of bulk migration or individually.
    /// </summary>
    Task<MigrationStatus> MigrateCharacterAsync(Guid characterId);

    /// <summary>
    /// Cancels an in-progress migration (Admin only).
    /// </summary>
    Task CancelMigrationAsync();
}

/// <summary>
/// Status of a season migration.
/// </summary>
[GenerateSerializer]
public record MigrationStatus
{
    [Id(0)] public required string SourceSeasonId { get; init; }
    [Id(1)] public string? TargetSeasonId { get; init; }
    [Id(2)] public MigrationState State { get; init; } = MigrationState.NotStarted;
    [Id(3)] public int TotalCharacters { get; init; }
    [Id(4)] public int MigratedCharacters { get; init; }
    [Id(5)] public int FailedCharacters { get; init; }
    [Id(6)] public DateTimeOffset? StartedAt { get; init; }
    [Id(7)] public DateTimeOffset? CompletedAt { get; init; }
    [Id(8)] public List<string> Errors { get; init; } = [];
}

public enum MigrationState
{
    NotStarted,
    InProgress,
    Completed,
    Failed,
    Cancelled
}
