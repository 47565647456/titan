using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// A single rate limit rule: MaxHits per PeriodSeconds, with TimeoutSeconds penalty on violation.
/// Format: "MaxHits:PeriodSeconds:TimeoutSeconds" (e.g., "10:60:300")
/// </summary>
[GenerateSerializer, MemoryPackable]
public partial record RateLimitRule(
    [property: Id(0), MemoryPackOrder(0)] int MaxHits,
    [property: Id(1), MemoryPackOrder(1)] int PeriodSeconds,
    [property: Id(2), MemoryPackOrder(2)] int TimeoutSeconds)
{
    /// <summary>
    /// Returns format string: "MaxHits:PeriodSeconds:TimeoutSeconds"
    /// </summary>
    public override string ToString() => $"{MaxHits}:{PeriodSeconds}:{TimeoutSeconds}";

    /// <summary>
    /// Parses a rule string.
    /// </summary>
    public static RateLimitRule Parse(string rule)
    {
        var parts = rule.Split(':');
        if (parts.Length != 3)
            throw new ArgumentException($"Invalid format: '{rule}'. Expected 'MaxHits:PeriodSeconds:TimeoutSeconds'");

        if (!int.TryParse(parts[0], out var maxHits) || maxHits <= 0)
            throw new ArgumentException($"Invalid MaxHits: '{parts[0]}'. Must be a positive integer.");
        if (!int.TryParse(parts[1], out var period) || period <= 0)
            throw new ArgumentException($"Invalid PeriodSeconds: '{parts[1]}'. Must be a positive integer.");
        if (!int.TryParse(parts[2], out var timeout) || timeout <= 0)
            throw new ArgumentException($"Invalid TimeoutSeconds: '{parts[2]}'. Must be a positive integer.");

        return new RateLimitRule(maxHits, period, timeout);
    }
}

/// <summary>
/// A named policy containing multiple rules (all must pass).
/// </summary>
[GenerateSerializer, MemoryPackable]
public partial record RateLimitPolicy(
    [property: Id(0), MemoryPackOrder(0)] string Name,
    [property: Id(1), MemoryPackOrder(1)] IReadOnlyList<RateLimitRule> Rules)
{
    /// <summary>
    /// Returns comma-separated rules for HTTP header.
    /// Format: "rule1,rule2,..." e.g. "10:60:300,100:3600:7200"
    /// </summary>
    public string ToHeaderValue() => string.Join(",", Rules.Select(r => r.ToString()));
}

/// <summary>
/// Configuration for which endpoints/hubs use which policy.
/// Supports glob patterns (e.g., "/api/auth/*", "TradeHub.*").
/// </summary>
[GenerateSerializer, MemoryPackable]
public partial record EndpointRateLimitConfig(
    [property: Id(0), MemoryPackOrder(0)] string Pattern,
    [property: Id(1), MemoryPackOrder(1)] string PolicyName);

/// <summary>
/// Current state for a single rule (for response headers).
/// Format: "CurrentHits:PeriodSeconds:SecondsUntilReset[:TimeoutRemaining]"
/// </summary>
[GenerateSerializer, MemoryPackable]
public partial record RateLimitRuleState(
    [property: Id(0), MemoryPackOrder(0)] int CurrentHits,
    [property: Id(1), MemoryPackOrder(1)] int PeriodSeconds,
    [property: Id(2), MemoryPackOrder(2)] int SecondsUntilReset,
    [property: Id(3), MemoryPackOrder(3)] int? TimeoutRemaining)
{
    /// <summary>
    /// Returns state string for HTTP header.
    /// </summary>
    public override string ToString() => TimeoutRemaining.HasValue
        ? $"{CurrentHits}:{PeriodSeconds}:{SecondsUntilReset}:{TimeoutRemaining}"
        : $"{CurrentHits}:{PeriodSeconds}:{SecondsUntilReset}";
}

/// <summary>
/// Global rate limiting configuration stored in grain.
/// </summary>
[GenerateSerializer, MemoryPackable]
public partial record RateLimitingConfiguration
{
    /// <summary>
    /// Master switch for rate limiting. Set to false for load testing.
    /// </summary>
    [Id(0), MemoryPackOrder(0)]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// All configured rate limit policies.
    /// </summary>
    [Id(1), MemoryPackOrder(1)]
    public IReadOnlyList<RateLimitPolicy> Policies { get; init; } = [];

    /// <summary>
    /// Mapping of endpoint patterns to policy names.
    /// </summary>
    [Id(2), MemoryPackOrder(2)]
    public IReadOnlyList<EndpointRateLimitConfig> EndpointMappings { get; init; } = [];

    /// <summary>
    /// Policy name to use when no endpoint mapping matches.
    /// </summary>
    [Id(3), MemoryPackOrder(3)]
    public string DefaultPolicyName { get; init; } = "Global";
}
