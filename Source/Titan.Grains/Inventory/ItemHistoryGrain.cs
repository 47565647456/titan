using MemoryPack;
using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Inventory;

[GenerateSerializer]
[MemoryPackable]
public partial class ItemHistoryGrainState
{
    [Id(0), MemoryPackOrder(0)] public List<ItemHistoryEntry> History { get; set; } = new();
}

public class ItemHistoryGrain : Grain, IItemHistoryGrain
{
    private readonly IPersistentState<ItemHistoryGrainState> _state;

    public ItemHistoryGrain(
        [PersistentState("itemHistory", "OrleansStorage")] IPersistentState<ItemHistoryGrainState> state)
    {
        _state = state;
    }

    public Task<List<ItemHistoryEntry>> GetHistoryAsync()
    {
        return Task.FromResult(_state.State.History);
    }

    public async Task AddEntryAsync(string eventType, Guid actorUserId, Guid? targetUserId = null, string? details = null)
    {
        var entry = new ItemHistoryEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = eventType,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            Details = details
        };

        _state.State.History.Add(entry);
        await _state.WriteStateAsync();
    }
}
