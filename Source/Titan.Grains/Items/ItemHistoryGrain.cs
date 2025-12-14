using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Titan.Abstractions;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;

namespace Titan.Grains.Items;

/// <summary>
/// State for ItemHistoryGrain.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial class ItemHistoryState
{
    /// <summary>
    /// List of history entries for this item.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public List<ItemHistoryEntry> Entries { get; set; } = new();
}

/// <summary>
/// Grain for tracking item history/audit trail.
/// Uses append-only log pattern with optional pruning.
/// </summary>
public class ItemHistoryGrain : Grain, IItemHistoryGrain
{
    private readonly IPersistentState<ItemHistoryState> _state;
    private readonly ItemHistoryOptions _options;
    private readonly ILogger<ItemHistoryGrain> _logger;

    public ItemHistoryGrain(
        [PersistentState("itemHistory", "OrleansStorage")] IPersistentState<ItemHistoryState> state,
        IOptions<ItemHistoryOptions> options,
        ILogger<ItemHistoryGrain> logger)
    {
        _state = state;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RecordEventAsync(
        string eventType,
        Guid? actorAccountId = null,
        Guid? actorCharacterId = null,
        Dictionary<string, string>? details = null)
    {
        var entry = new ItemHistoryEntry
        {
            EventId = Guid.NewGuid(),
            ItemId = this.GetPrimaryKey(),
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            ActorAccountId = actorAccountId,
            ActorCharacterId = actorCharacterId,
            Details = details
        };

        _state.State.Entries.Add(entry);

        // Prune old entries if over limit
        if (_state.State.Entries.Count > _options.MaxEntriesPerItem)
        {
            var toRemove = _state.State.Entries.Count - _options.MaxEntriesPerItem;
            _state.State.Entries.RemoveRange(0, toRemove);
            _logger.LogDebug("Pruned {Count} old history entries for item {ItemId}", toRemove, this.GetPrimaryKey());
        }

        await _state.WriteStateAsync();

        _logger.LogDebug("Recorded {EventType} event for item {ItemId}", eventType, this.GetPrimaryKey());
    }

    public Task<IReadOnlyList<ItemHistoryEntry>> GetHistoryAsync(int limit = 50)
    {
        var entries = _state.State.Entries
            .OrderBy(e => e.Timestamp)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<ItemHistoryEntry>>(entries);
    }

    public Task<IReadOnlyList<ItemHistoryEntry>> GetHistorySinceAsync(DateTimeOffset since)
    {
        var entries = _state.State.Entries
            .Where(e => e.Timestamp >= since)
            .OrderBy(e => e.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<ItemHistoryEntry>>(entries);
    }

    public Task<int> GetEntryCountAsync()
    {
        return Task.FromResult(_state.State.Entries.Count);
    }
}
