using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Events;

namespace Titan.API.Hubs;

/// <summary>
/// SignalR Hub for real-time season events.
/// Clients can subscribe to season updates and receive notifications.
/// </summary>
public class SeasonHub : Hub
{
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

    /// <summary>
    /// Notify clients about a season event for a specific season.
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

        // Notify specific season subscribers
        await hubContext.Clients.Group($"season-{seasonId}").SendAsync("SeasonEvent", seasonEvent);
        
        // Also notify global subscribers
        await hubContext.Clients.Group("all-seasons").SendAsync("SeasonEvent", seasonEvent);
    }

    /// <summary>
    /// Notify about migration progress.
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
}
