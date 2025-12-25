namespace Titan.API.Config;

/// <summary>
/// Configuration for rate limiting.
/// Policies are defined in code (RateLimitDefaults).
/// Endpoint mappings are defined via [RateLimitPolicy] attributes.
/// </summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Enable or disable rate limiting globally. Set to false for load testing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Redis connection name for rate limit state storage.
    /// </summary>
    public string RedisConnectionName { get; set; } = "rate-limiting";

    /// <summary>
    /// How long to cache configuration from grain (seconds).
    /// </summary>
    public int ConfigCacheSeconds { get; set; } = 30;
}

