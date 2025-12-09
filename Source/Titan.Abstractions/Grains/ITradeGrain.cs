using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing a trade session between two players.
/// Key: TradeId (Guid)
/// </summary>
public interface ITradeGrain : IGrainWithGuidKey
{
    Task<TradeSession> GetSessionAsync();
    
    /// <summary>
    /// Initializes a trade between initiator and target.
    /// </summary>
    Task<TradeSession> InitiateAsync(Guid initiatorUserId, Guid targetUserId);

    /// <summary>
    /// Adds an item to the trade (from either party).
    /// </summary>
    Task AddItemAsync(Guid userId, Guid itemId);

    /// <summary>
    /// Adds multiple items to the trade at once.
    /// </summary>
    Task AddItemsAsync(Guid userId, IEnumerable<Guid> itemIds);

    /// <summary>
    /// Removes an item from the trade.
    /// </summary>
    Task RemoveItemAsync(Guid userId, Guid itemId);

    /// <summary>
    /// Removes multiple items from the trade at once.
    /// </summary>
    Task RemoveItemsAsync(Guid userId, IEnumerable<Guid> itemIds);

    /// <summary>
    /// Accepts the trade (from one party). When both accept, trade executes.
    /// </summary>
    Task<TradeStatus> AcceptAsync(Guid userId);

    /// <summary>
    /// Cancels the trade.
    /// </summary>
    Task CancelAsync(Guid userId);
}
