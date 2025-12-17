using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Singleton grain for storing rate limit configuration.
/// Allows dynamic updates via admin UI without restarts.
/// Key: "default"
/// </summary>
public interface IRateLimitConfigGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets the current rate limiting configuration.
    /// </summary>
    Task<RateLimitingConfiguration> GetConfigurationAsync();

    /// <summary>
    /// Enables or disables rate limiting globally.
    /// </summary>
    Task SetEnabledAsync(bool enabled);

    /// <summary>
    /// Adds or updates a rate limit policy.
    /// </summary>
    Task<RateLimitPolicy> UpsertPolicyAsync(RateLimitPolicy policy);

    /// <summary>
    /// Removes a rate limit policy by name.
    /// </summary>
    Task RemovePolicyAsync(string policyName);

    /// <summary>
    /// Sets the default policy name.
    /// </summary>
    Task SetDefaultPolicyAsync(string policyName);

    /// <summary>
    /// Adds an endpoint-to-policy mapping.
    /// </summary>
    Task<EndpointRateLimitConfig> AddEndpointMappingAsync(EndpointRateLimitConfig mapping);

    /// <summary>
    /// Removes an endpoint mapping by pattern.
    /// </summary>
    Task RemoveEndpointMappingAsync(string pattern);

    /// <summary>
    /// Resets configuration to defaults (from appsettings).
    /// </summary>
    Task ResetToDefaultsAsync();

    /// <summary>
    /// Initializes with default configuration if not already configured.
    /// Called on startup.
    /// </summary>
    Task InitializeDefaultsAsync(RateLimitingConfiguration defaults);
}
