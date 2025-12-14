using Orleans;
using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Grains.Items;

/// <summary>
/// Grain for tracking item history/audit trail.
/// Key: ItemId
/// </summary>
public interface IItemHistoryGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Records an event in the item's history.
    /// </summary>
    /// <param name="eventType">Type of event (use ItemEventTypes constants).</param>
    /// <param name="actorAccountId">Account that performed the action.</param>
    /// <param name="actorCharacterId">Character that performed the action.</param>
    /// <param name="details">Additional event details.</param>
    Task RecordEventAsync(
        string eventType,
        Guid? actorAccountId = null,
        Guid? actorCharacterId = null,
        Dictionary<string, string>? details = null);

    /// <summary>
    /// Gets the item's history, optionally limited.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <returns>History entries in chronological order (oldest first).</returns>
    Task<IReadOnlyList<ItemHistoryEntry>> GetHistoryAsync(int limit = 50);

    /// <summary>
    /// Gets history entries since a specific time.
    /// </summary>
    Task<IReadOnlyList<ItemHistoryEntry>> GetHistorySinceAsync(DateTimeOffset since);

    /// <summary>
    /// Gets the total number of history entries.
    /// </summary>
    Task<int> GetEntryCountAsync();
}
