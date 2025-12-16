using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Orleans;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Config;
using Titan.API.Services.RateLimiting;

namespace Titan.API.Controllers;

/// <summary>
/// Admin API for managing rate limit configuration.
/// Used by Dashboard UI and integration tests.
/// </summary>
[ApiController]
[Route("api/admin/rate-limiting")]
[Authorize(Policy = "SuperAdmin")]
public class RateLimitAdminController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly RateLimitService _rateLimitService;
    private readonly IOptions<RateLimitingOptions> _options;
    private readonly ILogger<RateLimitAdminController> _logger;

    public RateLimitAdminController(
        IClusterClient clusterClient,
        RateLimitService rateLimitService,
        IOptions<RateLimitingOptions> options,
        ILogger<RateLimitAdminController> logger)
    {
        _clusterClient = clusterClient;
        _rateLimitService = rateLimitService;
        _options = options;
        _logger = logger;
    }

    private IRateLimitConfigGrain GetGrain() => _clusterClient.GetGrain<IRateLimitConfigGrain>("default");

    /// <summary>
    /// Get the current rate limiting configuration.
    /// </summary>
    [HttpGet("config")]
    public async Task<ActionResult<RateLimitingConfiguration>> GetConfiguration()
    {
        var config = await GetGrain().GetConfigurationAsync();
        return Ok(config);
    }

    /// <summary>
    /// Enable or disable rate limiting globally.
    /// </summary>
    [HttpPost("enabled")]
    public async Task<ActionResult> SetEnabled([FromBody] SetEnabledRequest request)
    {
        await GetGrain().SetEnabledAsync(request.Enabled);
        _rateLimitService.ClearCache(); // Invalidate cache to pick up change immediately
        _logger.LogInformation("Rate limiting {Status} by admin", request.Enabled ? "enabled" : "disabled");
        return Ok(new { success = true, enabled = request.Enabled });
    }

    /// <summary>
    /// Create or update a rate limit policy.
    /// </summary>
    [HttpPost("policies")]
    public async Task<ActionResult<RateLimitPolicy>> UpsertPolicy([FromBody] UpsertPolicyRequest request)
    {
        var rules = request.Rules.Select(RateLimitRule.Parse).ToList();
        var policy = new RateLimitPolicy(request.Name, rules);
        
        var result = await GetGrain().UpsertPolicyAsync(policy);
        _rateLimitService.ClearCache();
        _logger.LogInformation("Upserted policy {PolicyName} with {RuleCount} rules", policy.Name, rules.Count);
        
        return Ok(result);
    }

    /// <summary>
    /// Delete a rate limit policy.
    /// </summary>
    [HttpDelete("policies/{name}")]
    public async Task<ActionResult> RemovePolicy(string name)
    {
        await GetGrain().RemovePolicyAsync(name);
        _rateLimitService.ClearCache();
        _logger.LogInformation("Removed policy {PolicyName}", name);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Set the default policy used when no endpoint mapping matches.
    /// </summary>
    [HttpPost("default-policy")]
    public async Task<ActionResult> SetDefaultPolicy([FromBody] SetDefaultPolicyRequest request)
    {
        await GetGrain().SetDefaultPolicyAsync(request.PolicyName);
        _rateLimitService.ClearCache();
        _logger.LogInformation("Set default policy to {PolicyName}", request.PolicyName);
        return Ok(new { success = true, defaultPolicy = request.PolicyName });
    }

    /// <summary>
    /// Add or update an endpoint-to-policy mapping.
    /// </summary>
    [HttpPost("mappings")]
    public async Task<ActionResult<EndpointRateLimitConfig>> AddEndpointMapping([FromBody] AddEndpointMappingRequest request)
    {
        var mapping = new EndpointRateLimitConfig(request.Pattern, request.PolicyName);
        var result = await GetGrain().AddEndpointMappingAsync(mapping);
        _rateLimitService.ClearCache();
        _logger.LogInformation("Added endpoint mapping {Pattern} -> {PolicyName}", request.Pattern, request.PolicyName);
        return Ok(result);
    }

    /// <summary>
    /// Remove an endpoint-to-policy mapping.
    /// </summary>
    [HttpDelete("mappings/{pattern}")]
    public async Task<ActionResult> RemoveEndpointMapping(string pattern)
    {
        // URL decode the pattern since it may contain special chars
        var decodedPattern = Uri.UnescapeDataString(pattern);
        await GetGrain().RemoveEndpointMappingAsync(decodedPattern);
        _rateLimitService.ClearCache();
        _logger.LogInformation("Removed endpoint mapping {Pattern}", decodedPattern);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Reset all configuration to defaults from appsettings.
    /// </summary>
    [HttpPost("reset")]
    public async Task<ActionResult> ResetToDefaults()
    {
        return await PerformResetAsync();
    }

    private async Task<ActionResult> PerformResetAsync()
    {
        var grain = GetGrain();
        
        // First clear the state
        await grain.ResetToDefaultsAsync();
        
        // Then reinitialize with defaults from appsettings
        var opts = _options.Value;
        var defaults = new RateLimitingConfiguration
        {
            Enabled = opts.Enabled,
            DefaultPolicyName = opts.DefaultPolicyName,
            Policies = opts.DefaultPolicies
                .Select(p => new RateLimitPolicy(
                    p.Name, 
                    p.Rules.Select(RateLimitRule.Parse).ToList()))
                .ToList(),
            EndpointMappings = opts.DefaultEndpointMappings
                .Select(m => new EndpointRateLimitConfig(m.Pattern, m.PolicyName))
                .ToList()
        };
        
        await grain.InitializeDefaultsAsync(defaults);
        _rateLimitService.ClearCache();
        
        _logger.LogInformation("Reset rate limiting configuration to defaults with {PolicyCount} policies", 
            defaults.Policies.Count);
        return Ok(new { success = true, policiesRestored = defaults.Policies.Count });
    }
}

// Request DTOs
public record SetEnabledRequest(bool Enabled);
public record UpsertPolicyRequest(string Name, List<string> Rules);
public record SetDefaultPolicyRequest(string PolicyName);
public record AddEndpointMappingRequest(string Pattern, string PolicyName);

