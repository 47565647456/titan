using Microsoft.AspNetCore.SignalR;
using Orleans.Streams;
using Titan.Abstractions;
using Titan.Abstractions.Events;
using Titan.API.Hubs;
using Titan.API.Services.Encryption;

/// <summary>
/// Background service that subscribes to Orleans trade event streams
/// and forwards events to connected SignalR clients.
/// </summary>
public class TradeStreamSubscriber : BackgroundService
{
    private readonly IClusterClient _clusterClient;
    private readonly EncryptedHubBroadcaster<TradeHub> _broadcaster;
    private readonly ILogger<TradeStreamSubscriber> _logger;
    private readonly Dictionary<Guid, StreamSubscriptionHandle<TradeEvent>> _subscriptions = new();

    public TradeStreamSubscriber(
        IClusterClient clusterClient,
        EncryptedHubBroadcaster<TradeHub> broadcaster,
        ILogger<TradeStreamSubscriber> logger)
    {
        _clusterClient = clusterClient;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The subscriber is ready. Subscriptions are created dynamically when needed.
        _logger.LogInformation("TradeStreamSubscriber started. Ready to subscribe to trade events.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Subscribe to trade events for a specific trade.
    /// Called when a client joins a trade session via SignalR.
    /// </summary>
    public async Task SubscribeToTradeAsync(Guid tradeId)
    {
        if (_subscriptions.ContainsKey(tradeId))
            return;

        var streamProvider = _clusterClient.GetStreamProvider(TradeStreamConstants.ProviderName);
        var stream = streamProvider.GetStream<TradeEvent>(
            StreamId.Create(TradeStreamConstants.Namespace, tradeId));

        var subscription = await stream.SubscribeAsync(async (tradeEvent, token) =>
        {
            _logger.LogDebug("Received trade event: {EventType} for trade {TradeId}", 
                tradeEvent.EventType, tradeEvent.TradeId);

            // Forward to SignalR clients in the trade group (encrypted)
            await _broadcaster.SendToGroupAsync($"trade-{tradeId}", "TradeUpdate", new
            {
                TradeId = tradeEvent.TradeId,
                EventType = tradeEvent.EventType,
                Data = new
                {
                    Session = tradeEvent.Session,
                    UserId = tradeEvent.UserId,
                    ItemId = tradeEvent.ItemId
                },
                Timestamp = tradeEvent.Timestamp
            });
        });

        _subscriptions[tradeId] = subscription;
        _logger.LogInformation("Subscribed to trade stream for trade {TradeId}", tradeId);
    }

    /// <summary>
    /// Unsubscribe from trade events for a specific trade.
    /// Called when all clients leave a trade session.
    /// </summary>
    public async Task UnsubscribeFromTradeAsync(Guid tradeId)
    {
        if (_subscriptions.TryGetValue(tradeId, out var subscription))
        {
            await subscription.UnsubscribeAsync();
            _subscriptions.Remove(tradeId);
            _logger.LogInformation("Unsubscribed from trade stream for trade {TradeId}", tradeId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Clean up all subscriptions
        foreach (var subscription in _subscriptions.Values)
        {
            await subscription.UnsubscribeAsync();
        }
        _subscriptions.Clear();
        await base.StopAsync(cancellationToken);
    }
}
