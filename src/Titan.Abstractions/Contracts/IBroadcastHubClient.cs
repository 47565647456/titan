using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Server-to-client contract for broadcast messages.
/// Clients implement this to receive server push notifications.
/// </summary>
public interface IBroadcastHubClient
{
    /// <summary>
    /// Called when the server broadcasts a message to all players.
    /// </summary>
    /// <param name="message">The server message to display.</param>
    Task ReceiveServerMessage(ServerMessage message);
}
