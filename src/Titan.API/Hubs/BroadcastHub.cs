using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.Abstractions.Contracts;
using Titan.API.Services.Encryption;

namespace Titan.API.Hubs;

/// <summary>
/// SignalR hub for server broadcast messages.
/// Players connect to receive server-wide announcements.
/// </summary>
[Authorize]
public class BroadcastHub : TitanHubBase
{
    private const string AllPlayersGroup = "all-players";
    private readonly EncryptedHubBroadcaster<BroadcastHub> _broadcaster;
    private readonly ILogger<BroadcastHub> _logger;

    public BroadcastHub(
        IClusterClient clusterClient, 
        IEncryptionService encryptionService, 
        EncryptedHubBroadcaster<BroadcastHub> broadcaster,
        ILogger<BroadcastHub> logger)
        : base(clusterClient, encryptionService, logger)
    {
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            _broadcaster.RegisterConnection(Context.ConnectionId, userId);
        }
        await base.OnConnectedAsync();
        
        // Add to group via broadcaster for encrypted support
        _broadcaster.AddToGroup(Context.ConnectionId, AllPlayersGroup);
        _logger.LogDebug("Client {ConnectionId} joined broadcast group", Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _broadcaster.RemoveFromGroup(Context.ConnectionId, AllPlayersGroup);
        _broadcaster.UnregisterConnection(Context.ConnectionId);
        _logger.LogDebug("Client {ConnectionId} left broadcast group", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    #region Server-side Broadcast Helpers

    /// <summary>
    /// Broadcast a message to all connected players (for server-side use).
    /// </summary>
    public static async Task BroadcastToAllAsync(IHubContext<BroadcastHub> hubContext, ServerMessage message)
    {
        await hubContext.Clients.Group(AllPlayersGroup).SendAsync(nameof(IBroadcastHubClient.ReceiveServerMessage), message);
    }

    #endregion
}
