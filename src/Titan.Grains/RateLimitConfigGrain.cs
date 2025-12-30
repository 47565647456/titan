using MemoryPack;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.Abstractions.RateLimiting;

namespace Titan.Grains;

/// <summary>
/// Persistent grain for storing rate limit configuration.
/// Allows dynamic updates via admin UI without restarts.
/// </summary>
public class RateLimitConfigGrain : Grain, IRateLimitConfigGrain
{
    private readonly IPersistentState<RateLimitConfigState> _state;
    private readonly ILogger<RateLimitConfigGrain> _logger;

    public RateLimitConfigGrain(
        [PersistentState("ratelimitconfig", "GlobalStorage")] IPersistentState<RateLimitConfigState> state,
        ILogger<RateLimitConfigGrain> logger)
    {
        _state = state;
        _logger = logger;
    }

    public Task<RateLimitingConfiguration> GetConfigurationAsync()
    {
        return Task.FromResult(new RateLimitingConfiguration
        {
            Enabled = _state.State.Enabled,
            Policies = _state.State.Policies.Values.ToList(),
            EndpointMappings = _state.State.EndpointMappings.ToList(),
            DefaultPolicyName = _state.State.DefaultPolicyName,
            MetricsCollectionEnabled = _state.State.MetricsCollectionEnabled
        });
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        _state.State.Enabled = enabled;
        await _state.WriteStateAsync();
        _logger.LogInformation("Rate limiting {Status}", enabled ? "enabled" : "disabled");
    }

    public async Task<RateLimitPolicy> UpsertPolicyAsync(RateLimitPolicy policy)
    {
        _state.State.Policies[policy.Name] = policy;
        await _state.WriteStateAsync();
        _logger.LogInformation("Upserted rate limit policy: {PolicyName} with {RuleCount} rules",
            policy.Name, policy.Rules.Count);
        return policy;
    }

    public async Task RemovePolicyAsync(string policyName)
    {
        if (_state.State.Policies.Remove(policyName))
        {
            await _state.WriteStateAsync();
            _logger.LogInformation("Removed rate limit policy: {PolicyName}", policyName);
        }
    }

    public async Task SetDefaultPolicyAsync(string policyName)
    {
        // Validate policy exists in grain state OR in code-defined defaults
        if (!_state.State.Policies.ContainsKey(policyName) && 
            !RateLimitDefaults.IsDefaultPolicy(policyName))
        {
            throw new ArgumentException($"Policy '{policyName}' does not exist");
        }

        _state.State.DefaultPolicyName = policyName;
        await _state.WriteStateAsync();
        _logger.LogInformation("Set default rate limit policy: {PolicyName}", policyName);
    }

    public async Task<EndpointRateLimitConfig> AddEndpointMappingAsync(EndpointRateLimitConfig mapping)
    {
        // Validate pattern
        if (string.IsNullOrWhiteSpace(mapping.Pattern))
            throw new ArgumentException("Pattern cannot be empty");

        // Validate policy exists in grain state OR in code-defined defaults
        if (!_state.State.Policies.ContainsKey(mapping.PolicyName) && 
            !RateLimitDefaults.IsDefaultPolicy(mapping.PolicyName))
            throw new ArgumentException($"Policy '{mapping.PolicyName}' does not exist");

        // Remove existing mapping for same pattern
        _state.State.EndpointMappings.RemoveAll(m => m.Pattern == mapping.Pattern);
        _state.State.EndpointMappings.Add(mapping);
        await _state.WriteStateAsync();
        _logger.LogInformation("Added endpoint mapping: {Pattern} -> {PolicyName}",
            mapping.Pattern, mapping.PolicyName);
        return mapping;
    }

    public async Task RemoveEndpointMappingAsync(string pattern)
    {
        var removed = _state.State.EndpointMappings.RemoveAll(m => m.Pattern == pattern);
        if (removed > 0)
        {
            await _state.WriteStateAsync();
            _logger.LogInformation("Removed endpoint mapping: {Pattern}", pattern);
        }
    }

    public async Task SetMetricsCollectionEnabledAsync(bool enabled)
    {
        _state.State.MetricsCollectionEnabled = enabled;
        await _state.WriteStateAsync();
        _logger.LogInformation("Metrics collection {Status}", enabled ? "enabled" : "disabled");
    }

    public async Task ResetToDefaultsAsync()
    {
        // Restore from stored defaults if available
        if (_state.State.StoredDefaults != null)
        {
            _state.State.Enabled = _state.State.StoredDefaults.Enabled;
            _state.State.DefaultPolicyName = _state.State.StoredDefaults.DefaultPolicyName;
            _state.State.MetricsCollectionEnabled = _state.State.StoredDefaults.MetricsCollectionEnabled;
            _state.State.Policies.Clear();
            _state.State.EndpointMappings.Clear();
            
            foreach (var policy in _state.State.StoredDefaults.Policies)
            {
                _state.State.Policies[policy.Name] = policy;
            }
            foreach (var mapping in _state.State.StoredDefaults.EndpointMappings)
            {
                _state.State.EndpointMappings.Add(mapping);
            }
            
            await _state.WriteStateAsync();
            _logger.LogInformation("Reset rate limit configuration to stored defaults with {PolicyCount} policies", 
                _state.State.Policies.Count);
        }
        else
        {
            // No stored defaults, just clear
            _state.State = new RateLimitConfigState();
            await _state.WriteStateAsync();
            _logger.LogInformation("Reset rate limit configuration to empty (no stored defaults)");
        }
    }

    public async Task InitializeDefaultsAsync(RateLimitingConfiguration defaults)
    {
        // Always store defaults for future resets
        _state.State.StoredDefaults = defaults;
        
        // Only apply to config if no policies configured yet
        if (_state.State.Policies.Count == 0)
        {
            _state.State.Enabled = defaults.Enabled;
            _state.State.DefaultPolicyName = defaults.DefaultPolicyName;
            _state.State.MetricsCollectionEnabled = defaults.MetricsCollectionEnabled;

            foreach (var policy in defaults.Policies)
            {
                _state.State.Policies[policy.Name] = policy;
            }

            foreach (var mapping in defaults.EndpointMappings)
            {
                _state.State.EndpointMappings.Add(mapping);
            }
            
            _logger.LogInformation("Initialized rate limit configuration with {PolicyCount} policies and {MappingCount} mappings",
                defaults.Policies.Count, defaults.EndpointMappings.Count);
        }
        else
        {
            _logger.LogDebug("Rate limit configuration already has policies, stored defaults for future reset");
        }
        
        await _state.WriteStateAsync();
    }
}

/// <summary>
/// Persistent state for rate limit configuration.
/// </summary>
[GenerateSerializer, MemoryPackable]
public partial class RateLimitConfigState
{
    [Id(0), MemoryPackOrder(0)]
    public bool Enabled { get; set; } = true;

    [Id(1), MemoryPackOrder(1)]
    public Dictionary<string, RateLimitPolicy> Policies { get; set; } = new();

    [Id(2), MemoryPackOrder(2)]
    public List<EndpointRateLimitConfig> EndpointMappings { get; set; } = new();

    [Id(3), MemoryPackOrder(3)]
    public string DefaultPolicyName { get; set; } = "Global";

    [Id(4), MemoryPackOrder(4)]
    public bool MetricsCollectionEnabled { get; set; } = false;
    
    /// <summary>
    /// Stores the initial defaults for restoration on reset.
    /// </summary>
    [Id(5), MemoryPackOrder(5)]
    public RateLimitingConfiguration? StoredDefaults { get; set; }
}
