using Microsoft.Extensions.Options;
using Orleans;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.Abstractions.RateLimiting;
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
            
            // Use code-defined policies from RateLimitDefaults
            var defaults = new RateLimitingConfiguration
            {
                Enabled = config.Enabled,
                Policies = RateLimitDefaults.Policies.ToList(),
                EndpointMappings = [], // No default mappings - rely on attributes
                DefaultPolicyName = string.Empty
            };

            // Initialize grain with defaults (will skip if already configured)
            var grain = _clusterClient.GetGrain<IRateLimitConfigGrain>("default");
            await grain.InitializeDefaultsAsync(defaults);

            _logger.LogInformation("Rate limit configuration initialized with {PolicyCount} policies", 
                RateLimitDefaults.Policies.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize rate limit configuration");
        }
    }


    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
