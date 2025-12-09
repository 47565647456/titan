using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Events;

/// <summary>
/// Event published to Orleans Streams when trade state changes.
/// Subscribers (like the API) can forward these to SignalR clients.
/// </summary>
[GenerateSerializer]
[Alias("TradeEvent")]
public record TradeEvent
{
    /// <summary>
    /// The trade ID this event relates to.
    /// </summary>
    [Id(0)] public required Guid TradeId { get; init; }

    /// <summary>
    /// Type of event: Started, ItemAdded, ItemRemoved, Accepted, Completed, Cancelled, Expired, Failed
    /// </summary>
    [Id(1)] public required string EventType { get; init; }

    /// <summary>
    /// When the event occurred.
    /// </summary>
    [Id(2)] public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Current trade session state (optional, included for context).
    /// </summary>
    [Id(3)] public TradeSession? Session { get; init; }

    /// <summary>
    /// User who triggered this event (if applicable).
    /// </summary>
    [Id(4)] public Guid? UserId { get; init; }

    /// <summary>
    /// Item involved in this event (for ItemAdded/ItemRemoved).
    /// </summary>
    [Id(5)] public Guid? ItemId { get; init; }
}
