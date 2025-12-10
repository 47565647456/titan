using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Events;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for season operations.
/// Provides both CRUD/management operations and real-time notifications.
/// </summary>
public class SeasonHub : Hub
{
    private readonly IClusterClient _clusterClient;

    public SeasonHub(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    #region Subscriptions

    /// <summary>
    /// Join a season group to receive updates for that season.
    /// </summary>
    public async Task JoinSeasonGroup(string seasonId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"season-{seasonId}");
    }

    /// <summary>
    /// Leave a season group.
    /// </summary>
    public async Task LeaveSeasonGroup(string seasonId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"season-{seasonId}");
    }

    /// <summary>
    /// Join the global seasons group to receive all season events.
    /// </summary>
    public async Task JoinAllSeasonsGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-seasons");
    }

    /// <summary>
    /// Leave the global seasons group.
    /// </summary>
    public async Task LeaveAllSeasonsGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "all-seasons");
    }

    #endregion

    #region Season Operations

    /// <summary>
    /// Get all seasons (permanent and temporary).
    /// </summary>
    public async Task<IReadOnlyList<Season>> GetAllSeasons()
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        return await registry.GetAllSeasonsAsync();
    }

    /// <summary>
    /// Get the currently active temporary season.
    /// </summary>
    public async Task<Season?> GetCurrentSeason()
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        return await registry.GetCurrentSeasonAsync();
    }

    /// <summary>
    /// Get a specific season by ID.
    /// </summary>
    public async Task<Season?> GetSeason(string seasonId)
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        return await registry.GetSeasonAsync(seasonId);
    }

    /// <summary>
    /// Create a new season. Broadcasts to all season subscribers.
    /// </summary>
    public async Task<Season> CreateSeason(
        string seasonId,
        string name,
        SeasonType type,
        DateTimeOffset startDate,
        DateTimeOffset? endDate = null,
        SeasonStatus status = SeasonStatus.Upcoming,
        string? migrationTargetId = null,
        Dictionary<string, object>? modifiers = null)
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");

        var season = new Season
        {
            SeasonId = seasonId,
            Name = name,
            Type = type,
            StartDate = startDate,
            EndDate = endDate,
            Status = status,
            MigrationTargetId = migrationTargetId ?? "standard",
            Modifiers = modifiers
        };

        var created = await registry.CreateSeasonAsync(season);
        
        await NotifySeasonEvent(seasonId, "SeasonCreated", created);
        
        return created;
    }

    /// <summary>
    /// End a season and trigger migration.
    /// </summary>
    public async Task<Season> EndSeason(string seasonId)
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        var season = await registry.EndSeasonAsync(seasonId);
        
        await NotifySeasonEvent(seasonId, "SeasonEnded", season);
        
        return season;
    }

    /// <summary>
    /// Update a season's status.
    /// </summary>
    public async Task<Season> UpdateSeasonStatus(string seasonId, SeasonStatus status)
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        var season = await registry.UpdateSeasonStatusAsync(seasonId, status);
        
        await NotifySeasonEvent(seasonId, "SeasonStatusUpdated", season);
        
        return season;
    }

    #endregion

    #region Migration Operations

    /// <summary>
    /// Get migration status for a season.
    /// </summary>
    public async Task<MigrationStatus> GetMigrationStatus(string seasonId)
    {
        var grain = _clusterClient.GetGrain<ISeasonMigrationGrain>(seasonId);
        return await grain.GetStatusAsync();
    }

    /// <summary>
    /// Start migration for a season.
    /// </summary>
    public async Task<MigrationStatus> StartMigration(string seasonId, string? targetSeasonId = null)
    {
        var grain = _clusterClient.GetGrain<ISeasonMigrationGrain>(seasonId);
        return await grain.StartMigrationAsync(targetSeasonId ?? "standard");
    }

    /// <summary>
    /// Migrate a specific character.
    /// </summary>
    public async Task<MigrationStatus> MigrateCharacter(string seasonId, Guid characterId)
    {
        var grain = _clusterClient.GetGrain<ISeasonMigrationGrain>(seasonId);
        return await grain.MigrateCharacterAsync(characterId);
    }

    /// <summary>
    /// Cancel an in-progress migration.
    /// </summary>
    public async Task<MigrationStatus> CancelMigration(string seasonId)
    {
        var grain = _clusterClient.GetGrain<ISeasonMigrationGrain>(seasonId);
        await grain.CancelMigrationAsync();
        return await grain.GetStatusAsync();
    }

    #endregion

    #region Server Push Helpers

    private async Task NotifySeasonEvent(string seasonId, string eventType, object? data = null)
    {
        var seasonEvent = new
        {
            SeasonId = seasonId,
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            Data = data
        };

        // Notify specific season subscribers
        await Clients.Group($"season-{seasonId}").SendAsync("SeasonEvent", seasonEvent);

        // Also notify global subscribers
        await Clients.Group("all-seasons").SendAsync("SeasonEvent", seasonEvent);
    }

    /// <summary>
    /// Notify clients about a season event (for server-side use).
    /// </summary>
    public static async Task NotifySeasonEvent(
        IHubContext<SeasonHub> hubContext,
        string seasonId,
        string eventType,
        object? data = null)
    {
        var seasonEvent = new
        {
            SeasonId = seasonId,
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            Data = data
        };

        await hubContext.Clients.Group($"season-{seasonId}").SendAsync("SeasonEvent", seasonEvent);
        await hubContext.Clients.Group("all-seasons").SendAsync("SeasonEvent", seasonEvent);
    }

    /// <summary>
    /// Notify about migration progress (for server-side use).
    /// </summary>
    public static async Task NotifyMigrationProgress(
        IHubContext<SeasonHub> hubContext,
        string seasonId,
        int migratedCount,
        int totalCount,
        string state)
    {
        await NotifySeasonEvent(hubContext, seasonId, SeasonEventTypes.MigrationProgress, new
        {
            MigratedCount = migratedCount,
            TotalCount = totalCount,
            State = state,
            ProgressPercentage = totalCount > 0 ? (migratedCount * 100 / totalCount) : 0
        });
    }

    #endregion
}
