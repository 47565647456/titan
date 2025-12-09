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
/// Hosted service that seeds the item type registry from a JSON file on startup.
/// </summary>
public class ItemTypeSeedHostedService : IHostedService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ItemRegistryOptions _options;
    private readonly ILogger<ItemTypeSeedHostedService> _logger;

    public ItemTypeSeedHostedService(
        IGrainFactory grainFactory,
        IOptions<ItemRegistryOptions> options,
        ILogger<ItemTypeSeedHostedService> logger)
    {
        _grainFactory = grainFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.SeedFilePath))
        {
            _logger.LogInformation("ItemRegistry: No seed file path configured, skipping seeding.");
            return;
        }

        if (!File.Exists(_options.SeedFilePath))
        {
            _logger.LogWarning("ItemRegistry: Seed file not found at '{SeedFilePath}', skipping seeding.", _options.SeedFilePath);
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

            var json = await File.ReadAllTextAsync(_options.SeedFilePath, cancellationToken);
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
            _logger.LogInformation("ItemRegistry: Seeded {Count} item types from '{SeedFilePath}'.", definitions.Count, _options.SeedFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ItemRegistry: Failed to seed item types from '{SeedFilePath}'.", _options.SeedFilePath);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
