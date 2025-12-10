using Orleans;
using Orleans.Transactions.Abstractions;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing a trade session between two characters.
/// Key: TradeId (Guid)
/// Trade execution uses Orleans transactions for atomicity.
/// </summary>
public interface ITradeGrain : IGrainWithGuidKey
{
    Task<TradeSession> GetSessionAsync();
    
    /// <summary>
    /// Initializes a trade between two characters in the same season.
    /// Validates SSF restrictions on both characters.
    /// </summary>
    Task<TradeSession> InitiateAsync(Guid initiatorCharacterId, Guid targetCharacterId, string seasonId);

    /// <summary>
    /// Adds an item to the trade (from either party).
    /// </summary>
    Task AddItemAsync(Guid characterId, Guid itemId);

    /// <summary>
    /// Adds multiple items to the trade at once.
    /// </summary>
    Task AddItemsAsync(Guid characterId, IEnumerable<Guid> itemIds);

    /// <summary>
    /// Removes an item from the trade.
    /// </summary>
    Task RemoveItemAsync(Guid characterId, Guid itemId);

    /// <summary>
    /// Removes multiple items from the trade at once.
    /// </summary>
    Task RemoveItemsAsync(Guid characterId, IEnumerable<Guid> itemIds);

    /// <summary>
    /// Accepts the trade (from one party). When both accept, trade executes atomically.
    /// Uses Orleans transactions for atomic multi-grain item transfers.
    /// </summary>
    [Transaction(TransactionOption.Create)]
    Task<TradeStatus> AcceptAsync(Guid characterId);

    /// <summary>
    /// Cancels the trade.
    /// </summary>
    Task CancelAsync(Guid characterId);
}

