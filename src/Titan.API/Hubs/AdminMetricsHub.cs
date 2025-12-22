using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.API.Services.RateLimiting;
using Titan.API.Services.Encryption;
using Titan.Abstractions.Grains;

namespace Titan.API.Hubs;

/// <summary>
/// SignalR hub for admin dashboard real-time metrics.
/// Provides push updates for rate limiting metrics instead of polling.
/// </summary>
[Authorize(Policy = "AdminDashboard")]
public class AdminMetricsHub : TitanHubBase
{
    private readonly RateLimitService _rateLimitService;
    private readonly EncryptedHubBroadcaster<AdminMetricsHub> _broadcaster;
    private readonly ILogger<AdminMetricsHub> _logger;
    
    private const string MetricsGroup = "RateLimitMetrics";

    public AdminMetricsHub(
        IClusterClient clusterClient,
        IEncryptionService encryptionService,
        RateLimitService rateLimitService,
        EncryptedHubBroadcaster<AdminMetricsHub> broadcaster,
        ILogger<AdminMetricsHub> logger)
        : base(clusterClient, encryptionService, logger)
    {
        _rateLimitService = rateLimitService;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        _logger.LogInformation("Connection {ConnectionId} (user {UserId}) connected to AdminMetricsHub", 
            Context.ConnectionId, userId);
        
        if (!string.IsNullOrEmpty(userId))
        {
            _broadcaster.RegisterConnection(Context.ConnectionId, userId);
        }
        
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Connection {ConnectionId} disconnected from AdminMetricsHub", Context.ConnectionId);
        _broadcaster.UnregisterConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Formats rate limit metrics for client consumption.
    /// Shared across SubscribeToMetrics, RefreshMetrics, and AdminMetricsBroadcaster.
    /// </summary>
    public static object FormatMetrics(
        (int ActiveBuckets, int ActiveTimeouts,
        List<(string PartitionKey, string PolicyName, int PeriodSeconds, int CurrentCount, int SecondsRemaining)> Buckets,
        List<(string PartitionKey, string PolicyName, int SecondsRemaining)> Timeouts) metrics)
    {
        return new
        {
            metrics.ActiveBuckets,
            metrics.ActiveTimeouts,
            Buckets = metrics.Buckets.Select(b => new
            {
                b.PartitionKey,
                b.PolicyName,
                b.PeriodSeconds,
                b.CurrentCount,
                b.SecondsRemaining
            }).ToList(),
            Timeouts = metrics.Timeouts.Select(t => new
            {
                t.PartitionKey,
                t.PolicyName,
                t.SecondsRemaining
            }).ToList()
        };
    }

    /// <summary>
    /// Subscribe to rate limiting metrics updates.
    /// Client will receive "MetricsUpdated" events when metrics change.
    /// </summary>
    public async Task SubscribeToMetrics()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, MetricsGroup);
        _broadcaster.AddToGroup(Context.ConnectionId, MetricsGroup);
        _logger.LogDebug("Client {ConnectionId} subscribed to metrics", Context.ConnectionId);
        
        // Send current metrics immediately upon subscription (using encrypted sender)
        var metrics = await _rateLimitService.GetMetricsAsync();
        await _broadcaster.SendToConnectionAsync(Context.ConnectionId, "MetricsUpdated", FormatMetrics(metrics));
    }

    /// <summary>
    /// Unsubscribe from rate limiting metrics updates.
    /// </summary>
    public async Task UnsubscribeFromMetrics()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, MetricsGroup);
        _broadcaster.RemoveFromGroup(Context.ConnectionId, MetricsGroup);
        _logger.LogDebug("Client {ConnectionId} unsubscribed from metrics", Context.ConnectionId);
    }

    /// <summary>
    /// Request a refresh of the current metrics (manual refresh).
    /// </summary>
    public async Task RefreshMetrics()
    {
        var metrics = await _rateLimitService.GetMetricsAsync();
        await _broadcaster.SendToConnectionAsync(Context.ConnectionId, "MetricsUpdated", FormatMetrics(metrics));
    }

    /// <summary>
    /// Clear a rate limit timeout for a specific partition key and policy.
    /// Returns true if the timeout was found and cleared.
    /// </summary>
    public async Task<bool> ClearTimeout(string partitionKey, string policyName)
    {
        _logger.LogInformation("Admin {User} clearing timeout for {PartitionKey} on policy {Policy}", 
            Context.UserIdentifier, partitionKey, policyName);
        
        var result = await _rateLimitService.ClearTimeoutAsync(partitionKey, policyName);
        
        if (result)
        {
            // Send updated metrics to caller immediately
            await RefreshMetrics();
        }
        
        return result;
    }

    /// <summary>
    /// Clear all rate limit buckets (request counts) for a specific partition key.
    /// Returns the number of buckets cleared.
    /// </summary>
    public async Task<int> ClearBucket(string partitionKey)
    {
        _logger.LogInformation("Admin {User} clearing buckets for {PartitionKey}", 
            Context.UserIdentifier, partitionKey);
        
        var count = await _rateLimitService.ClearBucketAsync(partitionKey);
        
        if (count > 0)
        {
            // Send updated metrics to caller immediately
            await RefreshMetrics();
        }
        
        return count;
    }
}

/// <summary>
/// Service for broadcasting metrics updates to connected admin clients.
/// Uses debounce pattern - broadcasts after activity stops for the debounce interval.
/// Thread-safe and won't spam during high request volume.
/// Uses EncryptedHubBroadcaster for per-user encryption of background pushes.
/// </summary>
public class AdminMetricsBroadcaster : IDisposable
{
    private readonly EncryptedHubBroadcaster<AdminMetricsHub> _broadcaster;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AdminMetricsBroadcaster> _logger;
    
    private Timer? _debounceTimer;
    private readonly Lock _timerLock = new();
    private const int DebounceIntervalMs = 500; // Wait 500ms after last event

    public AdminMetricsBroadcaster(
        EncryptedHubBroadcaster<AdminMetricsHub> broadcaster,
        IServiceProvider serviceProvider,
        ILogger<AdminMetricsBroadcaster> logger)
    {
        _broadcaster = broadcaster;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Trigger a debounced metrics broadcast. Will broadcast 500ms after
    /// the last call to this method (coalescing rapid calls into one broadcast).
    /// </summary>
    public void TriggerBroadcast()
    {
        // Debounce: reset timer on each call, broadcast when timer fires
        using (_timerLock.EnterScope())
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                _ => _ = ExecuteBroadcastAsync(),
                null,
                DebounceIntervalMs,
                Timeout.Infinite // One-shot timer
            );
        }
    }

    private async Task ExecuteBroadcastAsync()
    {
        try
        {
            // Get fresh metrics at broadcast time (not from the original call)
            using var scope = _serviceProvider.CreateScope();
            var rateLimitService = scope.ServiceProvider.GetRequiredService<RateLimitService>();
            var metrics = await rateLimitService.GetMetricsAsync();
            
            // Record snapshot to Redis for historical tracking (pass metrics to avoid redundant fetch)
            await rateLimitService.RecordMetricsSnapshotAsync(metrics);
            
            // Use encrypted broadcaster for per-user encryption
            await _broadcaster.SendToGroupAsync("RateLimitMetrics", "MetricsUpdated", 
                AdminMetricsHub.FormatMetrics(metrics));
            
            _logger.LogDebug("Broadcast metrics update: {Buckets} buckets, {Timeouts} timeouts", 
                metrics.ActiveBuckets, metrics.ActiveTimeouts);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to broadcast metrics update");
        }
    }

    public void Dispose()
    {
        using (_timerLock.EnterScope())
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
