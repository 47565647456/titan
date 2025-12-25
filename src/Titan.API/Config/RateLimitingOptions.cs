namespace Titan.API.Config;

/// <summary>
/// Configuration for rate limiting.
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
    /// Policies to seed if grain has no configuration.
    /// Must be configured in appsettings.json - no hardcoded defaults.
    /// </summary>
    public List<PolicyConfig> DefaultPolicies { get; set; } = [];

    /// <summary>
    /// Endpoint-to-policy mappings.
    /// Must be configured in appsettings.json - no hardcoded defaults.
    /// All endpoints must have a mapping or requests will fail.
    /// </summary>
    public List<EndpointMappingConfig> DefaultEndpointMappings { get; set; } = [];

    /// <summary>
    /// Default policy name used when initializing the grain's configuration.
    /// Note: This is only used for grain seeding. The HTTP middleware throws 
    /// an exception for any endpoint without an explicit mapping in DefaultEndpointMappings.
    /// </summary>
    public string? DefaultPolicyName { get; set; }

    /// <summary>
    /// How long to cache configuration from grain (seconds).
    /// </summary>
    public int ConfigCacheSeconds { get; set; } = 30;
}

/// <summary>
/// Configuration for a rate limit policy.
/// </summary>
public class PolicyConfig
{
    /// <summary>
    /// Unique name for the policy.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Rules in format: "MaxHits:PeriodSeconds:TimeoutSeconds"
    /// Example: ["10:60:600", "30:300:1800"]
    /// </summary>
    public List<string> Rules { get; set; } = [];
}

/// <summary>
/// Configuration for endpoint-to-policy mapping.
/// </summary>
public class EndpointMappingConfig
{
    /// <summary>
    /// Pattern for matching endpoints. Supports glob (* for wildcard).
    /// HTTP: "/api/auth/*", SignalR: "TradeHub.*"
    /// </summary>
    public string Pattern { get; set; } = "";

    /// <summary>
    /// Name of the policy to apply.
    /// </summary>
    public string PolicyName { get; set; } = "";
}
