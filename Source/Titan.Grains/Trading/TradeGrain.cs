using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Trading;

public class TradeGrainState
{
    public TradeSession? Session { get; set; }
}

public class TradeGrain : Grain, ITradeGrain
{
    private readonly IPersistentState<TradeGrainState> _state;
    private readonly IGrainFactory _grainFactory;

    public TradeGrain(
        [PersistentState("trade", "OrleansStorage")] IPersistentState<TradeGrainState> state,
        IGrainFactory grainFactory)
    {
        _state = state;
        _grainFactory = grainFactory;
    }

    public Task<TradeSession> GetSessionAsync()
    {
        if (_state.State.Session == null)
            throw new InvalidOperationException("Trade session not initialized.");
        
        return Task.FromResult(_state.State.Session);
    }

    public async Task<TradeSession> InitiateAsync(Guid initiatorUserId, Guid targetUserId)
    {
        if (_state.State.Session != null)
            throw new InvalidOperationException("Trade session already exists.");

        _state.State.Session = new TradeSession
        {
            TradeId = this.GetPrimaryKey(),
            InitiatorUserId = initiatorUserId,
            TargetUserId = targetUserId,
            Status = TradeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _state.WriteStateAsync();
        return _state.State.Session;
    }

    public async Task AddItemAsync(Guid userId, Guid itemId)
    {
        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            throw new InvalidOperationException("Cannot modify a non-pending trade.");

        // Verify user owns the item
        var inventory = _grainFactory.GetGrain<IInventoryGrain>(userId);
        if (!await inventory.HasItemAsync(itemId))
            throw new InvalidOperationException("User does not own this item.");

        if (userId == session.InitiatorUserId)
        {
            _state.State.Session = session with
            {
                InitiatorItemIds = session.InitiatorItemIds.Append(itemId).Distinct().ToList()
            };
        }
        else if (userId == session.TargetUserId)
        {
            _state.State.Session = session with
            {
                TargetItemIds = session.TargetItemIds.Append(itemId).Distinct().ToList()
            };
        }
        else
        {
            throw new InvalidOperationException("User is not part of this trade.");
        }

        // Reset acceptance when items change
        _state.State.Session = _state.State.Session! with
        {
            InitiatorAccepted = false,
            TargetAccepted = false
        };

        await _state.WriteStateAsync();
    }

    public async Task RemoveItemAsync(Guid userId, Guid itemId)
    {
        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            throw new InvalidOperationException("Cannot modify a non-pending trade.");

        if (userId == session.InitiatorUserId)
        {
            _state.State.Session = session with
            {
                InitiatorItemIds = session.InitiatorItemIds.Where(id => id != itemId).ToList()
            };
        }
        else if (userId == session.TargetUserId)
        {
            _state.State.Session = session with
            {
                TargetItemIds = session.TargetItemIds.Where(id => id != itemId).ToList()
            };
        }

        // Reset acceptance
        _state.State.Session = _state.State.Session! with
        {
            InitiatorAccepted = false,
            TargetAccepted = false
        };

        await _state.WriteStateAsync();
    }

    public async Task<TradeStatus> AcceptAsync(Guid userId)
    {
        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            return session.Status;

        if (userId == session.InitiatorUserId)
            session = session with { InitiatorAccepted = true };
        else if (userId == session.TargetUserId)
            session = session with { TargetAccepted = true };
        else
            throw new InvalidOperationException("User is not part of this trade.");

        _state.State.Session = session;

        // Check if both accepted
        if (session.InitiatorAccepted && session.TargetAccepted)
        {
            await ExecuteTradeAsync();
        }

        await _state.WriteStateAsync();
        return _state.State.Session!.Status;
    }

    public async Task CancelAsync(Guid userId)
    {
        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            return;

        _state.State.Session = session with { Status = TradeStatus.Cancelled };
        await _state.WriteStateAsync();
    }

    private async Task ExecuteTradeAsync()
    {
        var session = _state.State.Session!;

        try
        {
            var initiatorInv = _grainFactory.GetGrain<IInventoryGrain>(session.InitiatorUserId);
            var targetInv = _grainFactory.GetGrain<IInventoryGrain>(session.TargetUserId);

            // Phase 1: Validate all items still exist
            foreach (var itemId in session.InitiatorItemIds)
            {
                if (!await initiatorInv.HasItemAsync(itemId))
                    throw new InvalidOperationException($"Initiator item {itemId} no longer available.");
            }
            foreach (var itemId in session.TargetItemIds)
            {
                if (!await targetInv.HasItemAsync(itemId))
                    throw new InvalidOperationException($"Target item {itemId} no longer available.");
            }

            // Phase 2: Execute transfers
            // Remove from initiator, record history
            foreach (var itemId in session.InitiatorItemIds)
            {
                var item = await initiatorInv.GetItemAsync(itemId);
                await initiatorInv.RemoveItemAsync(itemId);
                
                // Add to target with same properties
                if (item != null) await targetInv.ReceiveItemAsync(item);
                
                var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(itemId);
                await historyGrain.AddEntryAsync("Traded", session.InitiatorUserId, session.TargetUserId);
            }

            foreach (var itemId in session.TargetItemIds)
            {
                var item = await targetInv.GetItemAsync(itemId);
                await targetInv.RemoveItemAsync(itemId);
                
                if (item != null) await initiatorInv.ReceiveItemAsync(item);

                var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(itemId);
                await historyGrain.AddEntryAsync("Traded", session.TargetUserId, session.InitiatorUserId);
            }

            _state.State.Session = session with
            {
                Status = TradeStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception)
        {
            _state.State.Session = session with { Status = TradeStatus.Failed };
            throw;
        }
    }
}
