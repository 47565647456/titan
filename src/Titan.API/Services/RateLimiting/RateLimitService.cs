using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Orleans;
using StackExchange.Redis;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Config;
using Titan.API.Hubs;

namespace Titan.API.Services.RateLimiting;

/// <summary>
/// Rate limiting service with Redis-backed state.
/// Tracks per-account (or IP) request counts across multiple time windows.
/// </summary>
public partial class RateLimitService
{
    // Pattern cache for glob matching
    private static readonly ConcurrentDictionary<string, Regex> _patternCache = new();

    // Metrics
    private static readonly Meter _meter = new("Titan.RateLimiting", "1.0");
    private static readonly Counter<long> _requestsAllowed = _meter.CreateCounter<long>("ratelimit.requests.allowed", description: "Rate limit checks that passed");
    private static readonly Counter<long> _requestsDenied = _meter.CreateCounter<long>("ratelimit.requests.denied", description: "Rate limit checks that were denied");
    private static readonly Counter<long> _timeoutsTriggered = _meter.CreateCounter<long>("ratelimit.timeouts.triggered", description: "Rate limit timeouts triggered");

    private readonly IConnectionMultiplexer _redis;
    private readonly IClusterClient _clusterClient;
    private readonly IOptions<RateLimitingOptions> _options;
    private readonly ILogger<RateLimitService> _logger;
    private readonly AdminMetricsBroadcaster? _broadcaster;

    private RateLimitingConfiguration? _cachedConfig;
    private DateTimeOffset _configCacheExpiry;
    private readonly Lock _cacheLock = new();

    public RateLimitService(
        [FromKeyedServices("rate-limiting")] IConnectionMultiplexer redis,
        IClusterClient clusterClient,
        IOptions<RateLimitingOptions> options,
        ILogger<RateLimitService> logger,
        AdminMetricsBroadcaster? broadcaster = null)
    {
        _redis = redis;
        _clusterClient = clusterClient;
        _options = options;
        _logger = logger;
        _broadcaster = broadcaster;
    }

    /// <summary>
    /// Check and record a request. Returns rate limit result with state for headers.
    /// Uses Redis pipelining for efficient batch reads.
    /// </summary>
    public async Task<RateLimitResult> CheckAsync(string partitionKey, string policyName)
    {
        var config = await GetConfigAsync();
        if (!config.Enabled)
            return RateLimitResult.CreateAllowed();

        var policy = config.Policies.FirstOrDefault(p => p.Name == policyName)
            ?? config.Policies.FirstOrDefault(p => p.Name == config.DefaultPolicyName);

        if (policy == null)
            return RateLimitResult.CreateAllowed();

        var db = _redis.GetDatabase();
        var states = new List<RateLimitRuleState>();
        int? maxRetryAfter = null;
        bool allowed = true;
        bool timeoutTriggered = false;

        // Pipeline all Redis reads for efficiency
        var batch = db.CreateBatch();
        var ruleChecks = new List<(RateLimitRule rule, Task<TimeSpan?> timeoutTtl, Task<RedisValue> count, Task<TimeSpan?> counterTtl)>();

        foreach (var rule in policy.Rules)
        {
            var timeoutKey = GetTimeoutKey(partitionKey, policyName);
            var counterKey = GetCounterKey(partitionKey, policyName, rule.PeriodSeconds);

            ruleChecks.Add((
                rule,
                batch.KeyTimeToLiveAsync(timeoutKey),
                batch.StringGetAsync(counterKey),
                batch.KeyTimeToLiveAsync(counterKey)
            ));
        }

        batch.Execute();
        await Task.WhenAll(ruleChecks.SelectMany(r => new Task[] { r.timeoutTtl, r.count, r.counterTtl }));

        // Process pipelined results
        foreach (var (rule, timeoutTtlTask, countTask, counterTtlTask) in ruleChecks)
        {
            var timeoutTtl = await timeoutTtlTask;
            if (timeoutTtl.HasValue)
            {
                // Already in timeout
                var timeoutRemaining = (int)Math.Ceiling(timeoutTtl.Value.TotalSeconds);
                states.Add(new RateLimitRuleState(rule.MaxHits, rule.PeriodSeconds, 0, timeoutRemaining));
                allowed = false;
                maxRetryAfter = Math.Max(maxRetryAfter ?? 0, timeoutRemaining);
                continue;
            }

            var countValue = await countTask;
            var count = countValue.HasValue ? (int)countValue : 0;
            var counterTtl = await counterTtlTask;
            var secondsUntilReset = counterTtl.HasValue ? (int)Math.Ceiling(counterTtl.Value.TotalSeconds) : rule.PeriodSeconds;

            if (count >= rule.MaxHits)
            {
                // Trigger timeout (this write cannot be pipelined as it depends on the check)
                await db.StringSetAsync(GetTimeoutKey(partitionKey, policyName), "1", TimeSpan.FromSeconds(rule.TimeoutSeconds));
                states.Add(new RateLimitRuleState(count, rule.PeriodSeconds, 0, rule.TimeoutSeconds));
                allowed = false;
                maxRetryAfter = Math.Max(maxRetryAfter ?? 0, rule.TimeoutSeconds);
                timeoutTriggered = true;
                _logger.LogWarning("Rate limit timeout triggered for {PartitionKey} on policy {Policy}: {Timeout}s",
                    partitionKey, policyName, rule.TimeoutSeconds);
            }
            else
            {
                states.Add(new RateLimitRuleState(count, rule.PeriodSeconds, secondsUntilReset, null));
            }
        }

        // Only increment counters if all rules passed
        if (allowed)
        {
            foreach (var rule in policy.Rules)
            {
                await IncrementCounterAsync(db, partitionKey, policyName, rule);
            }
            _requestsAllowed.Add(1, new KeyValuePair<string, object?>("policy", policy.Name));
        }
        else
        {
            _requestsDenied.Add(1, new KeyValuePair<string, object?>("policy", policy.Name));
            if (timeoutTriggered)
            {
                _timeoutsTriggered.Add(1, new KeyValuePair<string, object?>("policy", policy.Name));
            }
            _logger.LogDebug("Rate limit exceeded for {PartitionKey} on policy {Policy}. Retry after {RetryAfter}s",
                partitionKey, policy.Name, maxRetryAfter);
        }

        // Trigger metrics broadcast (debounced) for real-time dashboard
        _broadcaster?.TriggerBroadcast();

        return new RateLimitResult(allowed, policy, states, maxRetryAfter);
    }

    /// <summary>
    /// Gets the policy for an endpoint/hub method, with caching.
    /// </summary>
    public async Task<RateLimitPolicy?> GetPolicyForEndpointAsync(string endpoint)
    {
        var config = await GetConfigAsync();
        if (!config.Enabled)
            return null;

        var mapping = config.EndpointMappings.FirstOrDefault(m => MatchesPattern(endpoint, m.Pattern));
        if (mapping is not null)
        {
            return config.Policies.FirstOrDefault(p => p.Name == mapping.PolicyName);
        }

        return config.Policies.FirstOrDefault(p => p.Name == config.DefaultPolicyName);
    }

    /// <summary>
    /// Gets the policy by name.
    /// </summary>
    public async Task<RateLimitPolicy?> GetPolicyAsync(string policyName)
    {
        var config = await GetConfigAsync();
        return config.Policies.FirstOrDefault(p => p.Name == policyName);
    }

    /// <summary>
    /// Checks if rate limiting is enabled.
    /// </summary>
    public async Task<bool> IsEnabledAsync()
    {
        var config = await GetConfigAsync();
        return config.Enabled;
    }

    /// <summary>
    /// Clears the configuration cache, forcing a reload from grain.
    /// </summary>
    public void ClearCache()
    {
        using (_cacheLock.EnterScope())
        {
            _cachedConfig = null;
            _configCacheExpiry = DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// Clears all rate limit state from Redis.
    /// Used for testing to reset rate limit counters between tests.
    /// </summary>
    public async Task ClearRateLimitStateAsync()
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServers().FirstOrDefault();

        if (server == null)
        {
            _logger.LogWarning("No Redis server found for clearing rate limit state");
            return;
        }

        var keysToDelete = new List<RedisKey>();

        // Find all rate limit keys (counters and timeouts)
        // Key format: rl|{partitionKey}|{policyName}|{periodSeconds} or rl|timeout|{partitionKey}|{policyName}
        await foreach (var key in server.KeysAsync(pattern: "rl|*"))
        {
            keysToDelete.Add(key);
        }

        if (keysToDelete.Count > 0)
        {
            await db.KeyDeleteAsync([.. keysToDelete]);
            _logger.LogInformation("Cleared {Count} rate limit keys from Redis", keysToDelete.Count);
        }
    }

    /// <summary>
    /// Clears a specific timeout from Redis.
    /// Returns true if the timeout was found and deleted, false otherwise.
    /// </summary>
    public async Task<bool> ClearTimeoutAsync(string partitionKey, string policyName)
    {
        var db = _redis.GetDatabase();
        var timeoutKey = GetTimeoutKey(partitionKey, policyName);

        var deleted = await db.KeyDeleteAsync(timeoutKey);

        if (deleted)
        {
            _logger.LogInformation("Cleared timeout for {PartitionKey} on policy {Policy}",
                partitionKey, policyName);

            // Trigger metrics broadcast so dashboard updates
            _broadcaster?.TriggerBroadcast();
        }
        else
        {
            _logger.LogDebug("No timeout found for {PartitionKey} on policy {Policy}",
                partitionKey, policyName);
        }

        return deleted;
    }

    /// <summary>
    /// Clears all rate limit buckets (counters) for a specific partition key.
    /// Returns the number of buckets deleted.
    /// </summary>
    public async Task<int> ClearBucketAsync(string partitionKey)
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServers().FirstOrDefault();

        if (server == null)
        {
            _logger.LogWarning("No Redis server found for clearing buckets");
            return 0;
        }

        var keysToDelete = new List<RedisKey>();

        // Find all counter keys for this partition key
        // Counter key format: rl|{partitionKey}|{policyName}|{periodSeconds}
        var pattern = $"rl|{partitionKey}|*";
        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            // Exclude timeout keys (they have "timeout" as the second segment)
            if (!key.ToString().StartsWith("rl|timeout|"))
            {
                keysToDelete.Add(key);
            }
        }

        if (keysToDelete.Count > 0)
        {
            await db.KeyDeleteAsync([.. keysToDelete]);
            _logger.LogInformation("Cleared {Count} buckets for {PartitionKey}",
                keysToDelete.Count, partitionKey);

            // Trigger metrics broadcast so dashboard updates
            _broadcaster?.TriggerBroadcast();
        }
        else
        {
            _logger.LogDebug("No buckets found for {PartitionKey}", partitionKey);
        }

        return keysToDelete.Count;
    }

    /// <summary>
    /// Gets current rate limit metrics from Redis.
    /// Returns active buckets and timeouts with their current state.
    /// </summary>
    public async Task<(int ActiveBuckets, int ActiveTimeouts,
        List<(string PartitionKey, string PolicyName, int PeriodSeconds, int CurrentCount, int SecondsRemaining)> Buckets,
        List<(string PartitionKey, string PolicyName, int SecondsRemaining)> Timeouts)> GetMetricsAsync()
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServers().FirstOrDefault();

        var buckets = new List<(string PartitionKey, string PolicyName, int PeriodSeconds, int CurrentCount, int SecondsRemaining)>();
        var timeouts = new List<(string PartitionKey, string PolicyName, int SecondsRemaining)>();

        if (server == null)
        {
            _logger.LogWarning("No Redis server found for getting metrics");
            return (0, 0, buckets, timeouts);
        }

        // Find all rate limit keys
        await foreach (var key in server.KeysAsync(pattern: "rl|*"))
        {
            var keyStr = key.ToString();
            var ttl = await db.KeyTimeToLiveAsync(key);
            var secondsRemaining = ttl.HasValue ? (int)Math.Ceiling(ttl.Value.TotalSeconds) : 0;

            if (keyStr.StartsWith("rl|timeout|"))
            {
                // Timeout key format: rl|timeout|{partitionKey}|{policyName}
                var parts = keyStr.Split('|', 4);
                if (parts.Length >= 4)
                {
                    var partitionKey = parts[2];
                    var policyName = parts[3];
                    timeouts.Add((partitionKey, policyName, secondsRemaining));
                }
            }
            else if (keyStr.StartsWith("rl|"))
            {
                // Counter key format: rl|{partitionKey}|{policyName}|{periodSeconds}
                var parts = keyStr.Split('|', 4);
                if (parts.Length >= 4)
                {
                    var partitionKey = parts[1];
                    var policyName = parts[2];
                    if (int.TryParse(parts[3], out var periodSeconds))
                    {
                        var countValue = await db.StringGetAsync(key);
                        var count = countValue.HasValue ? (int)countValue : 0;
                        buckets.Add((partitionKey, policyName, periodSeconds, count, secondsRemaining));
                    }
                }
            }

        }

        return (buckets.Count, timeouts.Count, buckets, timeouts);
    }

    /// <summary>
    /// Records current metrics snapshot to Redis for historical tracking.
    /// Called by the broadcaster after each metrics update.
    /// Only records if metrics collection is enabled.
    /// </summary>
    /// <param name="metrics">Pre-fetched metrics data (avoids redundant Redis scan).</param>
    public async Task RecordMetricsSnapshotAsync(
        (int ActiveBuckets, int ActiveTimeouts,
        List<(string PartitionKey, string PolicyName, int PeriodSeconds, int CurrentCount, int SecondsRemaining)> Buckets,
        List<(string PartitionKey, string PolicyName, int SecondsRemaining)> Timeouts) metrics)
    {
        // Check if metrics collection is enabled (default: disabled)
        if (!await IsMetricsCollectionEnabledAsync())
        {
            return;
        }

        var totalRequests = metrics.Buckets.Sum(b => b.CurrentCount);
        
        var snapshot = new MetricsSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActiveBuckets = metrics.ActiveBuckets,
            ActiveTimeouts = metrics.ActiveTimeouts,
            TotalRequests = totalRequests
        };
        
        var db = _redis.GetDatabase();
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        
        // LPUSH + LTRIM to maintain sliding window of 300 entries
        await db.ListLeftPushAsync(HistoryKey, json);
        await db.ListTrimAsync(HistoryKey, 0, MaxHistoryEntries - 1);
    }

    /// <summary>
    /// Records current metrics snapshot to Redis for historical tracking.
    /// This overload fetches metrics - prefer the overload that accepts pre-fetched metrics.
    /// </summary>
    public async Task RecordMetricsSnapshotAsync()
    {
        var metrics = await GetMetricsAsync();
        await RecordMetricsSnapshotAsync(metrics);
    }

    /// <summary>
    /// Gets historical metrics snapshots from Redis.
    /// </summary>
    /// <param name="count">Number of snapshots to return (clamped to MaxHistoryEntries).</param>
    /// <returns>List of snapshots ordered newest first.</returns>
    public async Task<List<MetricsSnapshot>> GetMetricsHistoryAsync(int count = 60)
    {
        count = Math.Clamp(count, 1, MaxHistoryEntries);
        var db = _redis.GetDatabase();
        var values = await db.ListRangeAsync(HistoryKey, 0, count - 1);
        
        var result = new List<MetricsSnapshot>();
        foreach (var value in values)
        {
            try
            {
                var snapshot = System.Text.Json.JsonSerializer.Deserialize<MetricsSnapshot>(value.ToString());
                if (snapshot != null)
                {
                    result.Add(snapshot);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Skip malformed entries
            }
        }
        
        return result;
    }

    /// <summary>
    /// Checks if metrics collection is enabled.
    /// Uses the cached configuration from the grain.
    /// </summary>
    public async Task<bool> IsMetricsCollectionEnabledAsync()
    {
        var config = await GetConfigAsync();
        return config.MetricsCollectionEnabled;
    }

    /// <summary>
    /// Enables or disables metrics collection via the grain.
    /// Clears the local cache to pick up the change immediately.
    /// </summary>
    public async Task SetMetricsCollectionEnabledAsync(bool enabled)
    {
        var grain = _clusterClient.GetGrain<IRateLimitConfigGrain>("default");
        await grain.SetMetricsCollectionEnabledAsync(enabled);
        ClearCache();
        _logger.LogInformation("Metrics collection {Status}", enabled ? "enabled" : "disabled");
    }

    /// <summary>
    /// Clears all historical metrics data.
    /// </summary>
    public async Task ClearMetricsHistoryAsync()
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(HistoryKey);
        _logger.LogInformation("Cleared metrics history");
    }

    private const string HistoryKey = "rl|history";
    private const int MaxHistoryEntries = 300;



    private async Task IncrementCounterAsync(IDatabase db, string partitionKey, string policyName, RateLimitRule rule)
    {
        var key = GetCounterKey(partitionKey, policyName, rule.PeriodSeconds);
        var newCount = await db.StringIncrementAsync(key);

        if (newCount == 1)
        {
            // First request in window - set expiry
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(rule.PeriodSeconds));
        }
    }

    private async Task<RateLimitingConfiguration> GetConfigAsync()
    {
        // Check cache first
        using (_cacheLock.EnterScope())
        {
            if (_cachedConfig != null && _configCacheExpiry > DateTimeOffset.UtcNow)
                return _cachedConfig;
        }

        RateLimitingConfiguration config;

        try
        {
            // Fetch from grain
            var grain = _clusterClient.GetGrain<IRateLimitConfigGrain>("default");
            config = await grain.GetConfigurationAsync();

            // If grain has no policies yet, use appsettings defaults
            if (config.Policies.Count == 0)
            {
                config = BuildDefaultConfig();
                _logger.LogDebug("Grain returned empty policies, using appsettings defaults");
            }
        }
        catch (Exception ex)
        {
            // If grain fails, fall back to appsettings
            _logger.LogWarning(ex, "Failed to get config from grain, using appsettings defaults");
            config = BuildDefaultConfig();
        }

        // Cache it
        using (_cacheLock.EnterScope())
        {
            _cachedConfig = config;
            _configCacheExpiry = DateTimeOffset.UtcNow.AddSeconds(_options.Value.ConfigCacheSeconds);
        }

        return config;
    }

    private RateLimitingConfiguration BuildDefaultConfig()
    {
        var opts = _options.Value;

        var policies = opts.DefaultPolicies
            .Select(p => new RateLimitPolicy(
                p.Name,
                p.Rules.Select(RateLimitRule.Parse).ToList()))
            .ToList();

        var mappings = opts.DefaultEndpointMappings
            .Select(m => new EndpointRateLimitConfig(m.Pattern, m.PolicyName))
            .ToList();

        return new RateLimitingConfiguration
        {
            Enabled = opts.Enabled,
            Policies = policies,
            EndpointMappings = mappings,
            DefaultPolicyName = opts.DefaultPolicyName ?? string.Empty
        };
    }

    private static string GetCounterKey(string partitionKey, string policyName, int periodSeconds)
        => $"rl|{partitionKey}|{policyName}|{periodSeconds}";

    private static string GetTimeoutKey(string partitionKey, string policyName)
        => $"rl|timeout|{partitionKey}|{policyName}";

    private static bool MatchesPattern(string endpoint, string pattern)
    {
        // Use cached compiled regex for performance
        var regex = _patternCache.GetOrAdd(pattern, p =>
        {
            var regexPattern = "^" + Regex.Escape(p).Replace("\\*", ".*") + "$";
            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
        return regex.IsMatch(endpoint);
    }
}

/// <summary>
/// Result of a rate limit check.
/// </summary>
public record RateLimitResult(
    bool IsAllowed,
    RateLimitPolicy? Policy,
    IReadOnlyList<RateLimitRuleState> States,
    int? RetryAfterSeconds)
{
    /// <summary>
    /// Returns a result indicating the request is allowed (rate limiting disabled or no policy).
    /// </summary>
    public static RateLimitResult CreateAllowed() => new(true, null, [], null);

    /// <summary>
    /// Gets the combined state string for HTTP headers.
    /// </summary>
    public string GetStateHeaderValue() => string.Join(",", States.Select(s => s.ToString()));
}

/// <summary>
/// Snapshot of rate limiting metrics at a point in time.
/// Used for historical tracking and graphing.
/// </summary>
public class MetricsSnapshot
{
    public DateTimeOffset Timestamp { get; init; }
    public int ActiveBuckets { get; init; }
    public int ActiveTimeouts { get; init; }
    public int TotalRequests { get; init; }
}
