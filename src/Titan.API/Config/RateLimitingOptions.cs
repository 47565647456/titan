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
    /// Default policies to seed if grain has no configuration.
    /// </summary>
    public List<PolicyConfig> DefaultPolicies { get; set; } =
    [
        new() { Name = "Global", Rules = ["100:60:300"] },
        new() { Name = "Auth", Rules = ["10:60:600", "30:300:1800"] },
        new() { Name = "Trade", Rules = ["60:60:120"] },
        new() { Name = "Relaxed", Rules = ["1000:60:60"] }
    ];

    /// <summary>
    /// Default endpoint-to-policy mappings.
    /// </summary>
    public List<EndpointMappingConfig> DefaultEndpointMappings { get; set; } =
    [
        new() { Pattern = "/api/auth/*", PolicyName = "Auth" },
        new() { Pattern = "TradeHub.*", PolicyName = "Trade" }
    ];

    /// <summary>
    /// Default policy name when no endpoint mapping matches.
    /// </summary>
    public string DefaultPolicyName { get; set; } = "Global";

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
