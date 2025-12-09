using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for tracking item history.
/// Key: ItemId (Guid)
/// </summary>
public interface IItemHistoryGrain : IGrainWithGuidKey
{
    Task<List<ItemHistoryEntry>> GetHistoryAsync();
    Task AddEntryAsync(string eventType, Guid actorUserId, Guid? targetUserId = null, string? details = null);
}
