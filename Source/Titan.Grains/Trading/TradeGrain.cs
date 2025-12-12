using MemoryPack;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Transactions;
using Titan.Abstractions;
using Titan.Abstractions.Events;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

using Titan.Abstractions.Rules;
using Titan.Grains.Trading.Rules;

namespace Titan.Grains.Trading;

[GenerateSerializer]
[MemoryPackable]
public partial class TradeGrainState
{
    [Id(0), MemoryPackOrder(0)] public TradeSession? Session { get; set; }
}

public class TradeGrain : Grain, ITradeGrain
{
    private readonly IPersistentState<TradeGrainState> _state;
    private readonly IGrainFactory _grainFactory;
    private readonly TradingOptions _options;
    private readonly IEnumerable<IRule<TradeRequestContext>> _rules;
    private IDisposable? _expirationTimer;
    private IAsyncStream<TradeEvent>? _tradeStream;

    public TradeGrain(
        [PersistentState("trade", "GlobalStorage")] IPersistentState<TradeGrainState> state,
        IGrainFactory grainFactory,
        IOptions<TradingOptions> options,
        IEnumerable<IRule<TradeRequestContext>> rules)
    {
        _state = state;
        _grainFactory = grainFactory;
        _options = options.Value;
        _rules = rules;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // ... (rest of method unchanged)
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
    private async Task PublishEventAsync(string eventType, Guid? characterId = null, Guid? itemId = null)
    {
        if (_tradeStream == null)
            return;

        await _tradeStream.OnNextAsync(new TradeEvent
        {
            TradeId = this.GetPrimaryKey(),
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            Session = _state.State.Session,
            UserId = characterId,
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

    public async Task<TradeSession> InitiateAsync(Guid initiatorCharacterId, Guid targetCharacterId, string seasonId)
    {
        if (_state.State.Session != null)
            throw new InvalidOperationException("Trade session already exists.");

        // Validate SSF restrictions on both characters
        var initiatorChar = _grainFactory.GetGrain<ICharacterGrain>(initiatorCharacterId, seasonId);
        var targetChar = _grainFactory.GetGrain<ICharacterGrain>(targetCharacterId, seasonId);

        var initiator = await initiatorChar.GetCharacterAsync();
        var target = await targetChar.GetCharacterAsync();

        // Run validation rules
        var context = new TradeRequestContext(initiator, target);
        foreach (var rule in _rules)
        {
            await rule.ValidateAsync(context);
        }

        _state.State.Session = new TradeSession
        {
            TradeId = this.GetPrimaryKey(),
            InitiatorCharacterId = initiatorCharacterId,
            TargetCharacterId = targetCharacterId,
            SeasonId = seasonId,
            Status = TradeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _state.WriteStateAsync();
        await PublishEventAsync("TradeStarted", initiatorCharacterId);
        return _state.State.Session;
    }

    public async Task AddItemAsync(Guid characterId, Guid itemId)
    {
        await AddItemsAsync(characterId, new[] { itemId });
    }

    public async Task AddItemsAsync(Guid characterId, IEnumerable<Guid> itemIds)
    {
        await EnsureNotExpiredAsync();

        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            throw new InvalidOperationException($"Cannot modify a trade with status: {session.Status}");

        var inventory = _grainFactory.GetGrain<IInventoryGrain>(characterId, session.SeasonId);
        var reader = _grainFactory.GetGrain<IItemTypeReaderGrain>("default");

        var itemIdList = itemIds.ToList();
        
        // Validate all items
        foreach (var itemId in itemIdList)
        {
            // Verify character owns the item
            var item = await inventory.GetItemAsync(itemId);
            if (item == null)
                throw new InvalidOperationException($"Character does not own item {itemId}.");

            // Check if item type is tradeable
            if (!await reader.IsTradeableAsync(item.ItemTypeId))
                throw new InvalidOperationException($"Item type '{item.ItemTypeId}' is not tradeable.");
        }

        // Determine which list to update
        List<Guid> currentItems;
        bool isInitiator = characterId == session.InitiatorCharacterId;
        
        if (isInitiator)
            currentItems = session.InitiatorItemIds.ToList();
        else if (characterId == session.TargetCharacterId)
            currentItems = session.TargetItemIds.ToList();
        else
            throw new InvalidOperationException("Character is not part of this trade.");

        // Add items (distinct)
        foreach (var itemId in itemIdList)
        {
            if (!currentItems.Contains(itemId))
                currentItems.Add(itemId);
        }

        // Check trade limit
        if (_options.MaxItemsPerUser > 0 && currentItems.Count > _options.MaxItemsPerUser)
            throw new InvalidOperationException($"Cannot add items: would exceed limit of {_options.MaxItemsPerUser} items per user.");

        // Update session
        if (isInitiator)
            _state.State.Session = session with { InitiatorItemIds = currentItems };
        else
            _state.State.Session = session with { TargetItemIds = currentItems };

        // Reset acceptance when items change
        _state.State.Session = _state.State.Session! with
        {
            InitiatorAccepted = false,
            TargetAccepted = false
        };

        await _state.WriteStateAsync();
        
        // Publish events for each item
        foreach (var itemId in itemIdList)
        {
            await PublishEventAsync("ItemAdded", characterId, itemId);
        }
    }

    public async Task RemoveItemAsync(Guid characterId, Guid itemId)
    {
        await RemoveItemsAsync(characterId, new[] { itemId });
    }

    public async Task RemoveItemsAsync(Guid characterId, IEnumerable<Guid> itemIds)
    {
        await EnsureNotExpiredAsync();

        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            throw new InvalidOperationException($"Cannot modify a trade with status: {session.Status}");

        var itemIdList = itemIds.ToList();

        if (characterId == session.InitiatorCharacterId)
        {
            _state.State.Session = session with
            {
                InitiatorItemIds = session.InitiatorItemIds.Where(id => !itemIdList.Contains(id)).ToList()
            };
        }
        else if (characterId == session.TargetCharacterId)
        {
            _state.State.Session = session with
            {
                TargetItemIds = session.TargetItemIds.Where(id => !itemIdList.Contains(id)).ToList()
            };
        }
        else
        {
            throw new InvalidOperationException("Character is not part of this trade.");
        }

        // Reset acceptance
        _state.State.Session = _state.State.Session! with
        {
            InitiatorAccepted = false,
            TargetAccepted = false
        };

        await _state.WriteStateAsync();
        
        // Publish events for each item
        foreach (var itemId in itemIdList)
        {
            await PublishEventAsync("ItemRemoved", characterId, itemId);
        }
    }

    public async Task<TradeStatus> AcceptAsync(Guid characterId)
    {
        await EnsureNotExpiredAsync();

        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            return session.Status;

        if (characterId == session.InitiatorCharacterId)
            session = session with { InitiatorAccepted = true };
        else if (characterId == session.TargetCharacterId)
            session = session with { TargetAccepted = true };
        else
            throw new InvalidOperationException("Character is not part of this trade.");

        _state.State.Session = session;

        // Check if both accepted
        if (session.InitiatorAccepted && session.TargetAccepted)
        {
            await ExecuteTradeAsync();
            await _state.WriteStateAsync();
            await PublishEventAsync("TradeCompleted", characterId);
        }
        else
        {
            await _state.WriteStateAsync();
            await PublishEventAsync("TradeAccepted", characterId);
        }

        return _state.State.Session!.Status;
    }

    public async Task CancelAsync(Guid characterId)
    {
        var session = await GetSessionAsync();
        if (session.Status != TradeStatus.Pending)
            return;

        _state.State.Session = session with { Status = TradeStatus.Cancelled };
        await _state.WriteStateAsync();
        await PublishEventAsync("TradeCancelled", characterId);
    }

    [Transaction(TransactionOption.CreateOrJoin)]
    private async Task ExecuteTradeAsync()
    {
        var session = _state.State.Session!;

        try
        {
            var initiatorInv = _grainFactory.GetGrain<IInventoryGrain>(session.InitiatorCharacterId, session.SeasonId);
            var targetInv = _grainFactory.GetGrain<IInventoryGrain>(session.TargetCharacterId, session.SeasonId);

            // Transfer initiator items to target (within transaction)
            foreach (var itemId in session.InitiatorItemIds)
            {
                var item = await initiatorInv.TransferItemOutAsync(itemId);
                if (item == null)
                    throw new InvalidOperationException($"Initiator item {itemId} no longer available.");
                
                await targetInv.TransferItemInAsync(item);

                // Record history (outside transaction, after success)
                var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(itemId);
                await historyGrain.AddEntryAsync("Traded", session.InitiatorCharacterId, session.TargetCharacterId);
            }

            // Transfer target items to initiator (within transaction)
            foreach (var itemId in session.TargetItemIds)
            {
                var item = await targetInv.TransferItemOutAsync(itemId);
                if (item == null)
                    throw new InvalidOperationException($"Target item {itemId} no longer available.");
                
                await initiatorInv.TransferItemInAsync(item);

                // Record history
                var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(itemId);
                await historyGrain.AddEntryAsync("Traded", session.TargetCharacterId, session.InitiatorCharacterId);
            }

            _state.State.Session = session with
            {
                Status = TradeStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception)
        {
            // Transaction will automatically rollback all inventory changes
            _state.State.Session = session with { Status = TradeStatus.Failed };
            await PublishEventAsync("TradeFailed");
            throw;
        }
    }
}
