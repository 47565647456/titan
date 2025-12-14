using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Titan.Abstractions;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Registry;

/// <summary>
/// Background service that seeds the item type registry from a JSON file on startup.
/// Uses BackgroundService to ensure Orleans silo is fully started before accessing grains.
/// </summary>
public class ItemTypeSeedHostedService : BackgroundService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ItemRegistryOptions _options;
    private readonly ILogger<ItemTypeSeedHostedService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public ItemTypeSeedHostedService(
        IGrainFactory grainFactory,
        IOptions<ItemRegistryOptions> options,
        ILogger<ItemTypeSeedHostedService> logger,
        IHostApplicationLifetime lifetime)
    {
        _grainFactory = grainFactory;
        _options = options.Value;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the application to be fully started (including Orleans silo)
        var tcs = new TaskCompletionSource();
        _lifetime.ApplicationStarted.Register(() => tcs.SetResult());
        await tcs.Task;
        
        // Small delay to ensure silo is fully operational
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        await SeedItemTypesAsync(stoppingToken);
    }

    private async Task SeedItemTypesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.SeedFilePath))
        {
            _logger.LogInformation("ItemRegistry: No seed file path configured, skipping seeding.");
            return;
        }

        // Resolve path relative to application directory (not working directory)
        var seedFilePath = Path.IsPathRooted(_options.SeedFilePath)
            ? _options.SeedFilePath
            : Path.Combine(AppContext.BaseDirectory, _options.SeedFilePath);

        if (!File.Exists(seedFilePath))
        {
            _logger.LogWarning("ItemRegistry: Seed file not found at '{SeedFilePath}', skipping seeding.", seedFilePath);
            return;
        }

        try
        {
            var registry = _grainFactory.GetGrain<IItemTypeRegistryGrain>("default");
            var existing = await registry.GetAllAsync();

            if (!_options.ForceSeed && existing.Count > 0)
            {
                _logger.LogInformation("ItemRegistry: Registry already has {Count} items, skipping seeding.", existing.Count);
                return;
            }

            var json = await File.ReadAllTextAsync(seedFilePath, cancellationToken);
            var definitions = JsonSerializer.Deserialize<List<ItemTypeDefinition>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (definitions == null || definitions.Count == 0)
            {
                _logger.LogWarning("ItemRegistry: Seed file is empty or invalid.");
                return;
            }

            await registry.RegisterManyAsync(definitions);
            _logger.LogInformation("ItemRegistry: Seeded {Count} item types from '{SeedFilePath}'.", definitions.Count, seedFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ItemRegistry: Failed to seed item types from '{SeedFilePath}'.", seedFilePath);
        }
    }
}
