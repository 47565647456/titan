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

            // Track presence (in-memory)
            var presenceGrain = _clusterClient.GetGrain<IPlayerPresenceGrain>(userId);
            await presenceGrain.RegisterConnectionAsync(Context.ConnectionId, GetType().Name);

            // Log session (persisted) - only on first connection for this user
            if (await presenceGrain.GetConnectionCountAsync() == 1)
            {
                var sessionGrain = _clusterClient.GetGrain<ISessionLogGrain>(userId);
                await sessionGrain.StartSessionAsync(ip);
            }

            _logger.LogDebug("User {UserId} connected via {Hub}", userId, GetType().Name);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.UserIdentifier != null)
        {
            var userId = GetUserId();
            var presenceGrain = _clusterClient.GetGrain<IPlayerPresenceGrain>(userId);
            await presenceGrain.UnregisterConnectionAsync(Context.ConnectionId);

            // End session (persisted) - only when last connection closes
            if (await presenceGrain.GetConnectionCountAsync() == 0)
            {
                var sessionGrain = _clusterClient.GetGrain<ISessionLogGrain>(userId);
                await sessionGrain.EndSessionAsync();
            }

            _logger.LogDebug("User {UserId} disconnected from {Hub}", userId, GetType().Name);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
