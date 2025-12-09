using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Streams;
using Titan.Abstractions;
using Titan.Abstractions.Events;
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
    private readonly TradingOptions _options;
    private IDisposable? _expirationTimer;
    private IAsyncStream<TradeEvent>? _tradeStream;

    public TradeGrain(
        [PersistentState("trade", "OrleansStorage")] IPersistentState<TradeGrainState> state,
        IGrainFactory grainFactory,
        IOptions<TradingOptions> options)
    {
        _state = state;
        _grainFactory = grainFactory;
        _options = options.Value;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Initialize the stream for publishing trade events
        var streamProvider = this.GetStreamProvider(TradeStreamConstants.ProviderName);
        _tradeStream = streamProvider.GetStream<TradeEvent>(
            StreamId.Create(TradeStreamConstants.Namespace, this.GetPrimaryKey()));

        // Only register timer if expiration is enabled
        if (_options.TradeTimeout > TimeSpan.Zero)
        {
            _expirationTimer = this.RegisterGrainTimer(
                CheckExpirationAsync,
                new GrainTimerCreationOptions
                {
                    DueTime = _options.ExpirationCheckInterval,
                    Period = _options.ExpirationCheckInterval,
                    Interleave = true
                });
        }

        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _expirationTimer?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    /// <summary>
    /// Publishes a trade event to the stream.
    /// </summary>
    private async Task PublishEventAsync(string eventType, Guid? userId = null, Guid? itemId = null)
    {
        if (_tradeStream == null)
            return;

        await _tradeStream.OnNextAsync(new TradeEvent
        {
            TradeId = this.GetPrimaryKey(),
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            Session = _state.State.Session,
            UserId = userId,
            ItemId = itemId
        });
    }

    private async Task CheckExpirationAsync(CancellationToken cancellationToken)
    {
        if (_state.State.Session == null)
            return;

        if (_state.State.Session.Status == TradeStatus.Pending &&
            _state.State.Session.CreatedAt + _options.TradeTimeout < DateTimeOffset.UtcNow)
        {
            _state.State.Session = _state.State.Session with { Status = TradeStatus.Expired };
            await _state.WriteStateAsync();
            await PublishEventAsync("TradeExpired");
        }
    }

    /// <summary>
    /// Checks if the trade has expired and updates status if needed.
    /// Called before any operation that requires a pending trade.
    /// </summary>
    private async Task EnsureNotExpiredAsync()
    {
        if (_state.State.Session == null)
            return;

        if (_state.State.Session.Status == TradeStatus.Pending &&
            _options.TradeTimeout > TimeSpan.Zero &&
            _state.State.Session.CreatedAt + _options.TradeTimeout < DateTimeOffset.UtcNow)
        {
            _state.State.Session = _state.State.Session with { Status = TradeStatus.Expired };
            await _state.WriteStateAsync();
            await PublishEventAsync("TradeExpired");
        }
    }

    public async Task<TradeSession> GetSessionAsync()
    {
        await EnsureNotExpiredAsync();

        if (_state.State.Session == null)
            throw new InvalidOperationException("Trade session not initialized.");

        return _state.State.Session;
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
        await PublishEventAsync("TradeStarted", initiatorUserId);
        return _state.State.Session;
    }

    public async Task AddItemAsync(Guid userId, Guid itemId)
    {
        await EnsureNotExpiredAsync();

        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            throw new InvalidOperationException($"Cannot modify a trade with status: {session.Status}");

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
        await PublishEventAsync("ItemAdded", userId, itemId);
    }

    public async Task RemoveItemAsync(Guid userId, Guid itemId)
    {
        await EnsureNotExpiredAsync();

        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            throw new InvalidOperationException($"Cannot modify a trade with status: {session.Status}");

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
        await PublishEventAsync("ItemRemoved", userId, itemId);
    }

    public async Task<TradeStatus> AcceptAsync(Guid userId)
    {
        await EnsureNotExpiredAsync();

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
            await _state.WriteStateAsync();
            await PublishEventAsync("TradeCompleted", userId);
        }
        else
        {
            await _state.WriteStateAsync();
            await PublishEventAsync("TradeAccepted", userId);
        }

        return _state.State.Session!.Status;
    }

    public async Task CancelAsync(Guid userId)
    {
        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            return;

        _state.State.Session = session with { Status = TradeStatus.Cancelled };
        await _state.WriteStateAsync();
        await PublishEventAsync("TradeCancelled", userId);
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
            foreach (var itemId in session.InitiatorItemIds)
            {
                var item = await initiatorInv.GetItemAsync(itemId);
                await initiatorInv.RemoveItemAsync(itemId);

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
            await PublishEventAsync("TradeFailed");
            throw;
        }
    }
}
