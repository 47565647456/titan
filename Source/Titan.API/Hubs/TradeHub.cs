using Microsoft.AspNetCore.SignalR;
using Titan.API.Services;

namespace Titan.API.Hubs;

/// <summary>
/// SignalR Hub for real-time trade events.
/// Clients can subscribe to trade updates and receive notifications.
/// </summary>
public class TradeHub : Hub
{
    private readonly TradeStreamSubscriber _streamSubscriber;

    public TradeHub(TradeStreamSubscriber streamSubscriber)
    {
        _streamSubscriber = streamSubscriber;
    }

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
        // Note: We don't unsubscribe here as other clients might still be listening
        // Cleanup happens when the trade completes or expires
    }

    /// <summary>
    /// Send a trade update to all participants.
    /// Called by the server when trade state changes (legacy - now handled by stream subscriber).
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
}
