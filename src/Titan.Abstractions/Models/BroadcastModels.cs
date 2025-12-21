using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Type of server broadcast message, used by clients for styling.
/// </summary>
public enum ServerMessageType
{
    /// <summary>General informational message.</summary>
    Info = 0,
    /// <summary>Warning that requires attention.</summary>
    Warning = 1,
    /// <summary>Error or critical alert.</summary>
    Error = 2,
    /// <summary>Player achievement announcement.</summary>
    Achievement = 3,
    /// <summary>Server maintenance notification.</summary>
    Maintenance = 4,
    /// <summary>Custom message type with client-defined styling.</summary>
    Custom = 5
}

/// <summary>
/// A message broadcast from the server to all connected players.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record ServerMessage
{
    /// <summary>
    /// Unique identifier for this message.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public Guid MessageId { get; init; }

    /// <summary>
    /// The message content to display.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public required string Content { get; init; }

    /// <summary>
    /// Type of message for client-side styling.
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public ServerMessageType Type { get; init; }

    /// <summary>
    /// Optional title/header for the message.
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public string? Title { get; init; }

    /// <summary>
    /// Optional icon identifier for client display.
    /// </summary>
    [Id(4), MemoryPackOrder(4)] public string? IconId { get; init; }

    /// <summary>
    /// How long to display the message in seconds. Null means until dismissed.
    /// </summary>
    [Id(5), MemoryPackOrder(5)] public int? DurationSeconds { get; init; }

    /// <summary>
    /// When the message was sent.
    /// </summary>
    [Id(6), MemoryPackOrder(6)] public DateTimeOffset Timestamp { get; init; }
}
