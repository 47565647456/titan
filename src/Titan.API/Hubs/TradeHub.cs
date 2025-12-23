using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services.Encryption;
using Titan.API.Services;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for trade operations.
/// All operations verify that the caller owns the character they're acting as.
/// </summary>
[Authorize]
public class TradeHub : TitanHubBase
{
    private readonly TradeStreamSubscriber _streamSubscriber;
    private readonly HubValidationService _validation;
    private readonly EncryptedHubBroadcaster<TradeHub> _broadcaster;

    public TradeHub(
        IClusterClient clusterClient, 
        IEncryptionService encryptionService, 
        TradeStreamSubscriber streamSubscriber, 
        HubValidationService validation, 
        EncryptedHubBroadcaster<TradeHub> broadcaster,
        ILogger<TradeHub> logger)
        : base(clusterClient, encryptionService, logger)
    {
        _streamSubscriber = streamSubscriber;
        _validation = validation;
        _broadcaster = broadcaster;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            _broadcaster.RegisterConnection(Context.ConnectionId, userId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            _broadcaster.UnregisterConnection(Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    #region Security Helpers

    // VerifyCharacterOwnershipAsync is inherited from TitanHubBase

    /// <summary>
    /// Gets the caller's owned character that is a participant in the specified trade.
    /// Throws if the caller is not a participant.
    /// </summary>
    private async Task<Guid> GetOwnedCharacterInTradeAsync(Guid tradeId)
    {
        var grain = ClusterClient.GetGrain<ITradeGrain>(tradeId);
        var session = await grain.GetSessionAsync();
        
        var accountGrain = ClusterClient.GetGrain<IAccountGrain>(GetUserId());
        var characters = await accountGrain.GetCharactersAsync();
        var characterIds = characters.Select(c => c.CharacterId).ToHashSet();

        if (characterIds.Contains(session.InitiatorCharacterId))
            return session.InitiatorCharacterId;
        
        if (characterIds.Contains(session.TargetCharacterId))
            return session.TargetCharacterId;
        
        throw new HubException("You are not a participant in this trade.");
    }

    #endregion

    #region Subscriptions

    /// <summary>
    /// Join a trade session group to receive updates (verifies caller is a participant).
    /// </summary>
    public async Task JoinTradeSession(Guid tradeId)
    {
        // Verify caller is a participant before allowing subscription
        await GetOwnedCharacterInTradeAsync(tradeId);
        
        await Groups.AddToGroupAsync(Context.ConnectionId, $"trade-{tradeId}");
        _broadcaster.AddToGroup(Context.ConnectionId, $"trade-{tradeId}");
        await _streamSubscriber.SubscribeToTradeAsync(tradeId);
    }

    /// <summary>
    /// Leave a trade session group.
    /// </summary>
    public async Task LeaveTradeSession(Guid tradeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trade-{tradeId}");
        _broadcaster.RemoveFromGroup(Context.ConnectionId, $"trade-{tradeId}");
    }

    #endregion

    #region Trade Lifecycle Operations

    /// <summary>
    /// Start a new trade session between your character and another character.
    /// </summary>
    /// <param name="myCharacterId">Your character (must belong to your account).</param>
    /// <param name="targetCharacterId">The character you want to trade with.</param>
    /// <param name="seasonId">The season for the trade.</param>
    public async Task<TradeSession> StartTrade(Guid myCharacterId, Guid targetCharacterId, string seasonId)
    {
        // Verify the caller owns the initiating character
        await VerifyCharacterOwnershipAsync(myCharacterId);
        await _validation.ValidateIdAsync(seasonId, nameof(seasonId));
        
        var tradeId = Guid.NewGuid();
        var grain = ClusterClient.GetGrain<ITradeGrain>(tradeId);

        var session = await grain.InitiateAsync(myCharacterId, targetCharacterId, seasonId);
        
        await NotifyTradeUpdate(tradeId, "TradeStarted", session);
        
        return session;
    }


    /// <summary>
    /// Get trade session details (verifies caller is a participant).
    /// </summary>
    public async Task<TradeSession> GetTrade(Guid tradeId)
    {
        // Verify caller is a participant
        await GetOwnedCharacterInTradeAsync(tradeId);
        
        var grain = ClusterClient.GetGrain<ITradeGrain>(tradeId);
        return await grain.GetSessionAsync();
    }

    /// <summary>
    /// Add an item to the trade. Automatically uses your character in the trade.
    /// </summary>
    public async Task<TradeSession> AddItem(Guid tradeId, Guid itemId)
    {
        var myCharacterId = await GetOwnedCharacterInTradeAsync(tradeId);
        
        var grain = ClusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.AddItemAsync(myCharacterId, itemId);
        var session = await grain.GetSessionAsync();

        await NotifyTradeUpdate(tradeId, "ItemAdded", new { CharacterId = myCharacterId, ItemId = itemId });

        return session;
    }

    /// <summary>
    /// Remove an item from the trade. Automatically uses your character in the trade.
    /// </summary>
    public async Task<TradeSession> RemoveItem(Guid tradeId, Guid itemId)
    {
        var myCharacterId = await GetOwnedCharacterInTradeAsync(tradeId);
        
        var grain = ClusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.RemoveItemAsync(myCharacterId, itemId);
        var session = await grain.GetSessionAsync();

        await NotifyTradeUpdate(tradeId, "ItemRemoved", new { CharacterId = myCharacterId, ItemId = itemId });

        return session;
    }

    /// <summary>
    /// Accept the trade offer. Automatically uses your character in the trade.
    /// </summary>
    public async Task<AcceptTradeResult> AcceptTrade(Guid tradeId)
    {
        var myCharacterId = await GetOwnedCharacterInTradeAsync(tradeId);
        
        var grain = ClusterClient.GetGrain<ITradeGrain>(tradeId);
        var status = await grain.AcceptAsync(myCharacterId);

        var eventType = status == TradeStatus.Completed ? "TradeCompleted" : "TradeAccepted";
        await NotifyTradeUpdate(tradeId, eventType, new { CharacterId = myCharacterId, Status = status.ToString() });

        return new AcceptTradeResult(status, status == TradeStatus.Completed);
    }

    /// <summary>
    /// Cancel the trade. Automatically uses your character in the trade.
    /// </summary>
    public async Task CancelTrade(Guid tradeId)
    {
        var myCharacterId = await GetOwnedCharacterInTradeAsync(tradeId);
        
        var grain = ClusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.CancelAsync(myCharacterId);

        await NotifyTradeUpdate(tradeId, "TradeCancelled", new { CharacterId = myCharacterId });
    }

    #endregion

    #region Server Push Helpers

    private async Task NotifyTradeUpdate(Guid tradeId, string eventType, object? data = null)
    {
        await _broadcaster.SendToGroupAsync($"trade-{tradeId}", "TradeUpdate", new
        {
            TradeId = tradeId,
            EventType = eventType,
            Data = data,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    #endregion
}

public record AcceptTradeResult(TradeStatus Status, bool Completed);

