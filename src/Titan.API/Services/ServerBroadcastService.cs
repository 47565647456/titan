using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Models;
using Titan.API.Hubs;

namespace Titan.API.Services;

/// <summary>
/// Service for broadcasting server messages to all connected players.
/// </summary>
public class ServerBroadcastService
{
    private readonly IHubContext<BroadcastHub> _hubContext;
    private readonly ILogger<ServerBroadcastService> _logger;

    public ServerBroadcastService(
        IHubContext<BroadcastHub> hubContext,
        ILogger<ServerBroadcastService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Broadcast a message to all connected players.
    /// </summary>
    /// <param name="message">The message to broadcast.</param>
    public async Task BroadcastAsync(ServerMessage message)
    {
        await BroadcastHub.BroadcastToAllAsync(_hubContext, message);
        _logger.LogInformation(
            "Broadcast message {MessageId} of type {Type}: {Title}",
            message.MessageId,
            message.Type,
            message.Title ?? "(no title)");
    }

    /// <summary>
    /// Create and broadcast a new server message.
    /// </summary>
    public async Task<ServerMessage> SendAsync(
        string content,
        ServerMessageType type = ServerMessageType.Info,
        string? title = null,
        string? iconId = null,
        int? durationSeconds = null)
    {
        var message = new ServerMessage
        {
            MessageId = Guid.NewGuid(),
            Content = content,
            Type = type,
            Title = title,
            IconId = iconId,
            DurationSeconds = durationSeconds,
            Timestamp = DateTimeOffset.UtcNow
        };

        await BroadcastAsync(message);
        return message;
    }
}
