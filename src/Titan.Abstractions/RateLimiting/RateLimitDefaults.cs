using System.Collections.Frozen;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.RateLimiting;

/// <summary>
/// Default rate limit policies defined in code.
/// Shared between API (for middleware fallback) and Grains (for validation).
/// Dashboard can still edit/add/delete policies at runtime.
/// </summary>
public static class RateLimitDefaults
{
    /// <summary>
    /// Default rate limit policies.
    /// Format: "MaxHits:PeriodSeconds:TimeoutSeconds"
    /// Note: Uses explicit List<> for Orleans/MemoryPack serialization when passed to grains.
    /// </summary>
    public static readonly IReadOnlyList<RateLimitPolicy> Policies =
    [
        new("Global",   new List<RateLimitRule> { RateLimitRule.Parse("100:60:300") }),
        new("Auth",     new List<RateLimitRule> { RateLimitRule.Parse("10:60:600"), RateLimitRule.Parse("30:300:1800") }),
        new("Trade",    new List<RateLimitRule> { RateLimitRule.Parse("60:60:120") }),
        new("Relaxed",  new List<RateLimitRule> { RateLimitRule.Parse("1000:60:60") }),
        new("Admin",    new List<RateLimitRule> { RateLimitRule.Parse("1000:60:120") }),
        new("AdminHub", new List<RateLimitRule> { RateLimitRule.Parse("5000:60:60") }),
        new("GameHub",  new List<RateLimitRule> { RateLimitRule.Parse("200:60:180") })
    ];
    
    /// <summary>
    /// Set of valid policy names from code-defined defaults.
    /// Uses FrozenSet for thread-safe, immutable, and fast lookup.
    /// </summary>
    public static readonly FrozenSet<string> PolicyNames = 
        Policies.Select(p => p.Name).ToFrozenSet();
    
    /// <summary>
    /// Checks if a policy name exists in the code-defined defaults.
    /// </summary>
    public static bool IsDefaultPolicy(string policyName) => PolicyNames.Contains(policyName);
}
