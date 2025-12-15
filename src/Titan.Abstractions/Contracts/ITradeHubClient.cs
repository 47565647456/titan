using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for TradeHub operations.
/// Used with TypedSignalR.Client source generator.
/// </summary>
public interface ITradeHubClient
{
    /// <summary>
    /// Join a trade session group to receive updates (verifies caller is a participant).
    /// </summary>
    Task JoinTradeSession(Guid tradeId);

    /// <summary>
    /// Leave a trade session group.
    /// </summary>
    Task LeaveTradeSession(Guid tradeId);

    /// <summary>
    /// Start a new trade session between your character and another character.
    /// </summary>
    Task<TradeSession> StartTrade(Guid myCharacterId, Guid targetCharacterId, string seasonId);

    /// <summary>
    /// Get trade session details (verifies caller is a participant).
    /// </summary>
    Task<TradeSession> GetTrade(Guid tradeId);

    /// <summary>
    /// Add an item to the trade. Automatically uses your character in the trade.
    /// </summary>
    Task<TradeSession> AddItem(Guid tradeId, Guid itemId);

    /// <summary>
    /// Remove an item from the trade. Automatically uses your character in the trade.
    /// </summary>
    Task<TradeSession> RemoveItem(Guid tradeId, Guid itemId);

    /// <summary>
    /// Accept the trade offer. Automatically uses your character in the trade.
    /// </summary>
    Task<AcceptTradeResult> AcceptTrade(Guid tradeId);

    /// <summary>
    /// Cancel the trade. Automatically uses your character in the trade.
    /// </summary>
    Task CancelTrade(Guid tradeId);
}

/// <summary>
/// Result of accepting a trade.
/// </summary>
public record AcceptTradeResult(TradeStatus Status, bool Completed);

/// <summary>
/// Server-to-client callback interface for receiving trade updates.
/// Register handlers for these methods on the client.
/// </summary>
public interface ITradeHubReceiver
{
    /// <summary>
    /// Called when a trade update occurs.
    /// </summary>
    Task TradeUpdate(TradeUpdateEvent update);
}

/// <summary>
/// Trade update event data.
/// </summary>
public record TradeUpdateEvent(Guid TradeId, string EventType, object? Data, DateTimeOffset Timestamp);
