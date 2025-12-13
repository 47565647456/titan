using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for SeasonHub operations.
/// Used with TypedSignalR.Client source generator.
/// </summary>
public interface ISeasonHubClient
{
    // Subscriptions
    Task JoinSeasonGroup(string seasonId);
    Task LeaveSeasonGroup(string seasonId);
    Task JoinAllSeasonsGroup();
    Task LeaveAllSeasonsGroup();

    // Read operations
    Task<IReadOnlyList<Season>> GetAllSeasons();
    Task<Season?> GetCurrentSeason();
    Task<Season?> GetSeason(string seasonId);

    // Admin operations
    Task<Season> CreateSeason(
        string seasonId,
        string name,
        SeasonType type,
        DateTimeOffset startDate,
        DateTimeOffset? endDate,
        SeasonStatus status,
        string? migrationTargetId,
        Dictionary<string, string>? modifiers,
        bool isVoid);
    Task<Season> EndSeason(string seasonId);
    Task<Season> UpdateSeasonStatus(string seasonId, SeasonStatus status);

    // Migration operations
    Task<MigrationStatus> GetMigrationStatus(string seasonId);
    Task<MigrationStatus> StartMigration(string seasonId, string? targetSeasonId);
    Task<MigrationStatus> MigrateCharacter(string seasonId, Guid characterId);
    Task<MigrationStatus> CancelMigration(string seasonId);
}
