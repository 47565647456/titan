using Microsoft.AspNetCore.SignalR;

namespace Titan.API.Hubs;

/// <summary>
/// SignalR Hub for real-time trade events.
/// Clients can subscribe to trade updates and receive notifications.
/// </summary>
public class TradeHub : Hub
{
    /// <summary>
    /// Join a trade session group to receive updates.
    /// </summary>
    public async Task JoinTradeSession(Guid tradeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"trade-{tradeId}");
    }

    /// <summary>
    /// Leave a trade session group.
    /// </summary>
    public async Task LeaveTradeSession(Guid tradeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trade-{tradeId}");
    }

    /// <summary>
    /// Send a trade update to all participants.
    /// Called by the server when trade state changes.
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
