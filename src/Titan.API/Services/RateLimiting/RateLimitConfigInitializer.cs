using Microsoft.Extensions.Options;
using Orleans;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Config;

namespace Titan.API.Services.RateLimiting;

/// <summary>
/// Hosted service that initializes rate limit configuration on startup.
/// Seeds default policies from appsettings if grain has no configuration.
/// </summary>
public class RateLimitConfigInitializer : IHostedService
{
    private readonly IClusterClient _clusterClient;
    private readonly IOptions<RateLimitingOptions> _options;
    private readonly ILogger<RateLimitConfigInitializer> _logger;

    public RateLimitConfigInitializer(
        IClusterClient clusterClient,
        IOptions<RateLimitingOptions> options,
        ILogger<RateLimitConfigInitializer> logger)
    {
        _clusterClient = clusterClient;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = _options.Value;
            
            // Convert appsettings config to domain models
            var policies = config.DefaultPolicies
                .Select(p => new RateLimitPolicy(
                    p.Name,
                    p.Rules.Select(RateLimitRule.Parse).ToList()))
                .ToList();

            var mappings = config.DefaultEndpointMappings
                .Select(m => new EndpointRateLimitConfig(m.Pattern, m.PolicyName))
                .ToList();

            var defaults = new RateLimitingConfiguration
            {
                Enabled = config.Enabled,
                Policies = policies,
                EndpointMappings = mappings,
                DefaultPolicyName = config.DefaultPolicyName ?? string.Empty
            };

            // Initialize grain with defaults (will skip if already configured)
            var grain = _clusterClient.GetGrain<IRateLimitConfigGrain>("default");
            await grain.InitializeDefaultsAsync(defaults);

            _logger.LogInformation("Rate limit configuration initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize rate limit configuration");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
