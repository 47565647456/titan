using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Titan.Abstractions.Grains;

namespace Titan.API.Hubs;

/// <summary>
/// Base class for authenticated hubs with connection lifecycle tracking.
/// Automatically tracks player presence and session logging.
/// </summary>
public abstract class TitanHubBase : Hub
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger _logger;

    protected TitanHubBase(IClusterClient clusterClient, ILogger logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the authenticated user's ID from the JWT token.
    /// </summary>
    protected Guid GetUserId() => Guid.Parse(Context.UserIdentifier!);

    /// <summary>
    /// Gets the cluster client for grain access.
    /// </summary>
    protected IClusterClient ClusterClient => _clusterClient;

    public override async Task OnConnectedAsync()
    {
        if (Context.UserIdentifier != null)
        {
            var userId = GetUserId();
            var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();

            // Track presence (in-memory) - returns true if this is first connection
            var presenceGrain = _clusterClient.GetGrain<IPlayerPresenceGrain>(userId);
            var isFirstConnection = await presenceGrain.RegisterConnectionAsync(Context.ConnectionId, GetType().Name);

            // Log session (persisted) - only on first connection for this user
            if (isFirstConnection)
            {
                var sessionGrain = _clusterClient.GetGrain<ISessionLogGrain>(userId);
                await sessionGrain.StartSessionAsync(ip);
            }

            _logger.LogDebug("User {UserId} connected via {Hub} (first: {IsFirst})", userId, GetType().Name, isFirstConnection);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.UserIdentifier != null)
        {
            var userId = GetUserId();
            
            // Track presence (in-memory) - returns true if this was last connection
            var presenceGrain = _clusterClient.GetGrain<IPlayerPresenceGrain>(userId);
            var wasLastConnection = await presenceGrain.UnregisterConnectionAsync(Context.ConnectionId);

            // End session (persisted) - only when last connection closes
            if (wasLastConnection)
            {
                var sessionGrain = _clusterClient.GetGrain<ISessionLogGrain>(userId);
                await sessionGrain.EndSessionAsync();
            }

            _logger.LogDebug("User {UserId} disconnected from {Hub} (last: {IsLast})", userId, GetType().Name, wasLastConnection);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
