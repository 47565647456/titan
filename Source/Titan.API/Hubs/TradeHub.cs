using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for trade operations.
/// Provides both trade lifecycle operations and real-time notifications.
/// </summary>
public class TradeHub : Hub
{
    private readonly IClusterClient _clusterClient;
    private readonly TradeStreamSubscriber _streamSubscriber;

    public TradeHub(IClusterClient clusterClient, TradeStreamSubscriber streamSubscriber)
    {
        _clusterClient = clusterClient;
        _streamSubscriber = streamSubscriber;
    }

    #region Subscriptions

    /// <summary>
    /// Join a trade session group to receive updates.
    /// This also subscribes to the Orleans stream for this trade.
    /// </summary>
    public async Task JoinTradeSession(Guid tradeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"trade-{tradeId}");
        await _streamSubscriber.SubscribeToTradeAsync(tradeId);
    }

    /// <summary>
    /// Leave a trade session group.
    /// </summary>
    public async Task LeaveTradeSession(Guid tradeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trade-{tradeId}");
    }

    #endregion

    #region Trade Lifecycle Operations

    /// <summary>
    /// Start a new trade session between two characters.
    /// </summary>
    public async Task<TradeSession> StartTrade(Guid initiatorCharacterId, Guid targetCharacterId, string seasonId)
    {
        var tradeId = Guid.NewGuid();
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);

        var session = await grain.InitiateAsync(initiatorCharacterId, targetCharacterId, seasonId);
        
        await NotifyTradeUpdate(tradeId, "TradeStarted", session);
        
        return session;
    }

    /// <summary>
    /// Get trade session details.
    /// </summary>
    public async Task<TradeSession> GetTrade(Guid tradeId)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        return await grain.GetSessionAsync();
    }

    /// <summary>
    /// Add an item to the trade.
    /// </summary>
    public async Task<TradeSession> AddItem(Guid tradeId, Guid characterId, Guid itemId)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.AddItemAsync(characterId, itemId);
        var session = await grain.GetSessionAsync();

        await NotifyTradeUpdate(tradeId, "ItemAdded", new { CharacterId = characterId, ItemId = itemId });

        return session;
    }

    /// <summary>
    /// Remove an item from the trade.
    /// </summary>
    public async Task<TradeSession> RemoveItem(Guid tradeId, Guid characterId, Guid itemId)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.RemoveItemAsync(characterId, itemId);
        var session = await grain.GetSessionAsync();

        await NotifyTradeUpdate(tradeId, "ItemRemoved", new { CharacterId = characterId, ItemId = itemId });

        return session;
    }

    /// <summary>
    /// Accept the trade offer.
    /// </summary>
    public async Task<AcceptTradeResult> AcceptTrade(Guid tradeId, Guid characterId)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        var status = await grain.AcceptAsync(characterId);

        var eventType = status == TradeStatus.Completed ? "TradeCompleted" : "TradeAccepted";
        await NotifyTradeUpdate(tradeId, eventType, new { CharacterId = characterId, Status = status.ToString() });

        return new AcceptTradeResult(status, status == TradeStatus.Completed);
    }

    /// <summary>
    /// Cancel the trade.
    /// </summary>
    public async Task CancelTrade(Guid tradeId, Guid characterId)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.CancelAsync(characterId);

        await NotifyTradeUpdate(tradeId, "TradeCancelled", new { CharacterId = characterId });
    }

    #endregion

    #region Server Push Helpers

    private async Task NotifyTradeUpdate(Guid tradeId, string eventType, object? data = null)
    {
        await Clients.Group($"trade-{tradeId}").SendAsync("TradeUpdate", new
        {
            TradeId = tradeId,
            EventType = eventType,
            Data = data,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Send a trade update to all participants (for server-side use).
    /// </summary>
    public static async Task NotifyTradeUpdate(IHubContext<TradeHub> hubContext, Guid tradeId, string eventType, object? data = null)
    {
        await hubContext.Clients.Group($"trade-{tradeId}").SendAsync("TradeUpdate", new
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
