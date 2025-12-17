using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Titan.Abstractions;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;

namespace Titan.Grains.Hosting;

/// <summary>
/// Orleans startup task that seeds base types, modifiers, and unique definitions after silo joins cluster.
/// Using IStartupTask ensures the silo is fully active before seeding.
/// </summary>
public class BaseTypeSeedStartupTask : IStartupTask
{
    private readonly IGrainFactory _grainFactory;
    private readonly BaseTypeSeedOptions _options;
    private readonly ILogger<BaseTypeSeedStartupTask> _logger;

    /// <summary>
    /// JSON serializer options configured to handle records with 'required' properties.
    /// Uses a TypeInfoResolver modifier to strip IsRequired constraints.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                static typeInfo =>
                {
                    if (typeInfo.Kind != JsonTypeInfoKind.Object) return;
                    foreach (var prop in typeInfo.Properties)
                    {
                        // Allow required properties to be missing (they have defaults)
                        prop.IsRequired = false;
                    }
                }
            }
        }
    };

    public BaseTypeSeedStartupTask(
        IGrainFactory grainFactory,
        IOptions<BaseTypeSeedOptions> options,
        ILogger<BaseTypeSeedStartupTask> logger)
    {
        _grainFactory = grainFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting base type seeding...");

            // Check if already populated
            var baseTypeRegistry = _grainFactory.GetGrain<IBaseTypeRegistryGrain>("default");
            var existingTypes = await baseTypeRegistry.GetAllAsync();

            if (existingTypes.Count > 0 && _options.SkipIfPopulated && !_options.ForceReseed)
            {
                _logger.LogInformation("Registry already populated with {Count} base types. Skipping seed.", existingTypes.Count);
                return;
            }

            // Load seed data
            var seedData = await LoadSeedDataAsync(cancellationToken);
            if (seedData == null)
            {
                _logger.LogError("No seed data found. Please check the seed data file.");
                return;
            }

            // Seed base types
            if (seedData.BaseTypes != null)
            {
                foreach (var baseType in seedData.BaseTypes)
                {
                    await baseTypeRegistry.RegisterAsync(baseType);
                    _logger.LogDebug("Registered base type: {BaseTypeId}", baseType.BaseTypeId);
                }
                _logger.LogInformation("Seeded {Count} base types.", seedData.BaseTypes.Count);
            }

            // Seed modifiers
            if (seedData.Modifiers != null)
            {
                var modifierRegistry = _grainFactory.GetGrain<IModifierRegistryGrain>("default");
                foreach (var modifier in seedData.Modifiers)
                {
                    await modifierRegistry.RegisterAsync(modifier);
                    _logger.LogDebug("Registered modifier: {ModifierId}", modifier.ModifierId);
                }
                _logger.LogInformation("Seeded {Count} modifiers.", seedData.Modifiers.Count);
            }

            // Seed uniques
            if (seedData.Uniques != null)
            {
                var uniqueRegistry = _grainFactory.GetGrain<IUniqueRegistryGrain>("default");
                foreach (var unique in seedData.Uniques)
                {
                    await uniqueRegistry.RegisterAsync(unique);
                    _logger.LogDebug("Registered unique: {UniqueId}", unique.UniqueId);
                }
                _logger.LogInformation("Seeded {Count} unique definitions.", seedData.Uniques.Count);
            }

            _logger.LogInformation("Base type seeding completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed base types.");
            throw; // IStartupTask should fail-fast if seeding fails
        }
    }

    private async Task<SeedData?> LoadSeedDataAsync(CancellationToken cancellationToken)
    {
        // Priority 1: Load from file if path is specified (for overrides)
        if (!string.IsNullOrEmpty(_options.SeedFilePath) && File.Exists(_options.SeedFilePath))
        {
            _logger.LogInformation("Loading seed data from file override: {Path}", _options.SeedFilePath);
            var json = await File.ReadAllTextAsync(_options.SeedFilePath, cancellationToken);
            return JsonSerializer.Deserialize<SeedData>(json, JsonOptions);
        }

        // Priority 2: Load from embedded resource
        var assembly = typeof(BaseTypeSeedStartupTask).Assembly;
        const string resourceName = "Titan.Grains.Data.item-seed-data.json";
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            _logger.LogInformation("Loading seed data from embedded resource.");
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(cancellationToken);
            return JsonSerializer.Deserialize<SeedData>(json, JsonOptions);
        }

        // Priority 3: Use hard-coded defaults as fallback
        _logger.LogError("Item Seed Data not found. Please check the seed data file.");
        return null;
    }
}

/// <summary>
/// Container for seed data loaded from JSON.
/// </summary>
public class SeedData
{
    public List<BaseType>? BaseTypes { get; set; }
    public List<ModifierDefinition>? Modifiers { get; set; }
    public List<UniqueDefinition>? Uniques { get; set; }
}
