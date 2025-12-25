using FluentValidation;
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
[Tags("Admin - Rate Limiting")]
[Authorize(Policy = "SuperAdmin")]
public class RateLimitAdminController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly RateLimitService _rateLimitService;
    private readonly IOptions<RateLimitingOptions> _options;
    private readonly ILogger<RateLimitAdminController> _logger;
    private readonly IValidator<UpsertPolicyRequest> _policyValidator;
    private readonly IValidator<AddEndpointMappingRequest> _mappingValidator;
    private readonly IValidator<SetDefaultPolicyRequest> _defaultPolicyValidator;

    public RateLimitAdminController(
        IClusterClient clusterClient,
        RateLimitService rateLimitService,
        IOptions<RateLimitingOptions> options,
        ILogger<RateLimitAdminController> logger,
        IValidator<UpsertPolicyRequest> policyValidator,
        IValidator<AddEndpointMappingRequest> mappingValidator,
        IValidator<SetDefaultPolicyRequest> defaultPolicyValidator)
    {
        _clusterClient = clusterClient;
        _rateLimitService = rateLimitService;
        _options = options;
        _logger = logger;
        _policyValidator = policyValidator;
        _mappingValidator = mappingValidator;
        _defaultPolicyValidator = defaultPolicyValidator;
    }

    private IRateLimitConfigGrain GetGrain() => _clusterClient.GetGrain<IRateLimitConfigGrain>("default");

    /// <summary>
    /// Get the current rate limiting configuration.
    /// </summary>
    /// <returns>Current rate limiting settings, policies, and endpoint mappings.</returns>
    [HttpGet("config")]
    [ProducesResponseType<RateLimitingConfiguration>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RateLimitingConfiguration>> GetConfiguration()
    {
        var config = await GetGrain().GetConfigurationAsync();
        return Ok(config);
    }

    /// <summary>
    /// Enable or disable rate limiting globally.
    /// </summary>
    /// <param name="request">Enable/disable setting.</param>
    /// <returns>Confirmation with new enabled status.</returns>
    [HttpPost("enabled")]
    [ProducesResponseType(StatusCodes.Status200OK)]
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
    /// <param name="request">Policy name and rules.</param>
    /// <returns>The created or updated policy.</returns>
    [HttpPost("policies")]
    [ProducesResponseType<RateLimitPolicy>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RateLimitPolicy>> UpsertPolicy([FromBody] UpsertPolicyRequest request)
    {
        var validationResult = await _policyValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

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
    /// <param name="name">Policy name to delete.</param>
    /// <returns>Success confirmation.</returns>
    [HttpDelete("policies/{name}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RemovePolicy(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return BadRequest(new { error = "Invalid policy name" });
        }

        await GetGrain().RemovePolicyAsync(name);
        _rateLimitService.ClearCache();
        _logger.LogInformation("Removed policy {PolicyName}", name);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Set the default policy used when no endpoint mapping matches.
    /// </summary>
    /// <param name="request">The policy name to use as default.</param>
    /// <returns>Success confirmation.</returns>
    [HttpPost("default-policy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> SetDefaultPolicy([FromBody] SetDefaultPolicyRequest request)
    {
        var validationResult = await _defaultPolicyValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        await GetGrain().SetDefaultPolicyAsync(request.PolicyName);
        _rateLimitService.ClearCache();
        _logger.LogInformation("Set default policy to {PolicyName}", request.PolicyName);
        return Ok(new { success = true, defaultPolicy = request.PolicyName });
    }

    /// <summary>
    /// Add or update an endpoint-to-policy mapping.
    /// </summary>
    /// <param name="request">Endpoint pattern and policy name.</param>
    /// <returns>The created or updated mapping.</returns>
    [HttpPost("mappings")]
    [ProducesResponseType<EndpointRateLimitConfig>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EndpointRateLimitConfig>> AddEndpointMapping([FromBody] AddEndpointMappingRequest request)
    {
        var validationResult = await _mappingValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var mapping = new EndpointRateLimitConfig(request.Pattern, request.PolicyName);
        var result = await GetGrain().AddEndpointMappingAsync(mapping);
        _rateLimitService.ClearCache();
        _logger.LogInformation("Added endpoint mapping {Pattern} -> {PolicyName}", request.Pattern, request.PolicyName);
        return Ok(result);
    }

    /// <summary>
    /// Remove an endpoint-to-policy mapping.
    /// </summary>
    /// <param name="pattern">URL-encoded endpoint pattern to remove.</param>
    /// <returns>Success confirmation.</returns>
    [HttpDelete("mappings/{pattern}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RemoveEndpointMapping(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 500)
        {
            return BadRequest(new { error = "Invalid pattern" });
        }

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
    /// <returns>Confirmation with number of policies restored.</returns>
    [HttpPost("reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
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
            DefaultPolicyName = opts.DefaultPolicyName ?? string.Empty,
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

    /// <summary>
    /// Get current rate limiting metrics from Redis.
    /// Shows active rate limit buckets, their counts and TTLs.
    /// </summary>
    /// <returns>Current rate limiting metrics including active buckets and timeouts.</returns>
    [HttpGet("metrics")]
    [ProducesResponseType<RateLimitMetrics>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RateLimitMetrics>> GetMetrics()
    {
        var (activeBuckets, activeTimeouts, buckets, timeouts) = await _rateLimitService.GetMetricsAsync();
        
        var result = new RateLimitMetrics(
            activeBuckets,
            activeTimeouts,
            buckets.Select(b => new RateLimitBucket(b.PartitionKey, b.PolicyName, b.PeriodSeconds, b.CurrentCount, b.SecondsRemaining)).ToList(),
            timeouts.Select(t => new RateLimitTimeout(t.PartitionKey, t.PolicyName, t.SecondsRemaining)).ToList()
        );
        
        return Ok(result);
    }

    /// <summary>
    /// Get historical rate limiting metrics for graphing.
    /// Returns up to the specified number of snapshots, ordered newest first.
    /// </summary>
    /// <param name="count">Number of snapshots to return (1-300, default 60).</param>
    /// <returns>Historical metrics snapshots for charting.</returns>
    [HttpGet("metrics/history")]
    [ProducesResponseType<List<MetricsHistoryItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MetricsHistoryItem>>> GetMetricsHistory([FromQuery] int count = 60)
    {
        count = Math.Clamp(count, 1, 300);
        var history = await _rateLimitService.GetMetricsHistoryAsync(count);
        
        var result = history.Select(h => new MetricsHistoryItem(
            h.Timestamp,
            h.ActiveBuckets,
            h.ActiveTimeouts,
            h.TotalRequests
        )).ToList();
        
        return Ok(result);
    }

    /// <summary>
    /// Get metrics collection status.
    /// </summary>
    /// <returns>Whether metrics collection is enabled.</returns>
    [HttpGet("metrics/collection")]
    [ProducesResponseType<MetricsCollectionStatus>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MetricsCollectionStatus>> GetMetricsCollectionStatus()
    {
        var enabled = await _rateLimitService.IsMetricsCollectionEnabledAsync();
        return Ok(new MetricsCollectionStatus(enabled));
    }

    /// <summary>
    /// Enable or disable metrics collection.
    /// Metrics collection is disabled by default.
    /// </summary>
    /// <param name="request">Enable/disable setting.</param>
    /// <returns>Confirmation with new status.</returns>
    [HttpPost("metrics/collection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> SetMetricsCollection([FromBody] SetEnabledRequest request)
    {
        await _rateLimitService.SetMetricsCollectionEnabledAsync(request.Enabled);
        _logger.LogInformation("Metrics collection {Status} by admin", request.Enabled ? "enabled" : "disabled");
        return Ok(new { success = true, enabled = request.Enabled });
    }

    /// <summary>
    /// Clear all collected metrics history.
    /// </summary>
    /// <returns>Confirmation of deletion.</returns>
    [HttpDelete("metrics/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> ClearMetricsHistory()
    {
        await _rateLimitService.ClearMetricsHistoryAsync();
        return Ok(new { success = true });
    }
}

// Request DTOs
public record SetEnabledRequest(bool Enabled);
public record UpsertPolicyRequest(string Name, List<string> Rules);
public record SetDefaultPolicyRequest(string PolicyName);
public record AddEndpointMappingRequest(string Pattern, string PolicyName);

// Metrics DTOs
public record RateLimitMetrics(
    int ActiveBuckets,
    int ActiveTimeouts,
    List<RateLimitBucket> Buckets,
    List<RateLimitTimeout> Timeouts);

public record RateLimitBucket(
    string PartitionKey,
    string PolicyName,
    int PeriodSeconds,
    int CurrentCount,
    int SecondsRemaining);

public record RateLimitTimeout(
    string PartitionKey,
    string PolicyName,
    int SecondsRemaining);

public record MetricsHistoryItem(
    DateTimeOffset Timestamp,
    int ActiveBuckets,
    int ActiveTimeouts,
    int TotalRequests);

public record MetricsCollectionStatus(bool Enabled);
