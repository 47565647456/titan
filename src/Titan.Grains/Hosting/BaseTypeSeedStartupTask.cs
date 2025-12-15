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
                _logger.LogWarning("No seed data found. Skipping seed.");
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
        _logger.LogInformation("Using hard-coded default seed data.");
        return GetDefaultSeedData();
    }

    private static SeedData GetDefaultSeedData()
    {
        return new SeedData
        {
            BaseTypes = new List<BaseType>
            {
                // Weapons
                new BaseType
                {
                    BaseTypeId = "simple_sword",
                    Name = "Simple Sword",
                    Description = "A basic one-handed sword.",
                    Category = ItemCategory.Equipment,
                    Slot = EquipmentSlot.MainHand,
                    Width = 1,
                    Height = 3,
                    RequiredLevel = 1,
                    MaxSockets = 3,
                    IsTradeable = true,
                    Tags = new HashSet<string> { "weapon", "sword", "melee", "one_handed" }
                },
                new BaseType
                {
                    BaseTypeId = "iron_sword",
                    Name = "Iron Sword",
                    Description = "A sturdy iron sword.",
                    Category = ItemCategory.Equipment,
                    Slot = EquipmentSlot.MainHand,
                    Width = 1,
                    Height = 3,
                    RequiredLevel = 10,
                    RequiredStrength = 20,
                    MaxSockets = 3,
                    IsTradeable = true,
                    Tags = new HashSet<string> { "weapon", "sword", "melee", "one_handed" }
                },
                new BaseType
                {
                    BaseTypeId = "long_bow",
                    Name = "Long Bow",
                    Description = "A two-handed ranged weapon.",
                    Category = ItemCategory.Equipment,
                    Slot = EquipmentSlot.MainHand,
                    Width = 2,
                    Height = 4,
                    RequiredLevel = 5,
                    RequiredDexterity = 15,
                    MaxSockets = 6,
                    IsTradeable = true,
                    Tags = new HashSet<string> { "weapon", "bow", "ranged", "two_handed" }
                },
                // Armor
                new BaseType
                {
                    BaseTypeId = "leather_vest",
                    Name = "Leather Vest",
                    Description = "Light body armour.",
                    Category = ItemCategory.Equipment,
                    Slot = EquipmentSlot.BodyArmour,
                    Width = 2,
                    Height = 3,
                    RequiredLevel = 1,
                    MaxSockets = 4,
                    IsTradeable = true,
                    Tags = new HashSet<string> { "armour", "body", "evasion" }
                },
                new BaseType
                {
                    BaseTypeId = "iron_helmet",
                    Name = "Iron Helmet",
                    Description = "A protective iron helmet.",
                    Category = ItemCategory.Equipment,
                    Slot = EquipmentSlot.Helmet,
                    Width = 2,
                    Height = 2,
                    RequiredLevel = 8,
                    RequiredStrength = 15,
                    MaxSockets = 4,
                    IsTradeable = true,
                    Tags = new HashSet<string> { "armour", "helmet", "armour" }
                },
                // Accessories
                new BaseType
                {
                    BaseTypeId = "gold_ring",
                    Name = "Gold Ring",
                    Description = "A simple gold ring.",
                    Category = ItemCategory.Equipment,
                    Slot = EquipmentSlot.RingLeft,
                    Width = 1,
                    Height = 1,
                    RequiredLevel = 1,
                    MaxSockets = 0,
                    IsTradeable = true,
                    Tags = new HashSet<string> { "accessory", "ring" }
                },
                new BaseType
                {
                    BaseTypeId = "leather_belt",
                    Name = "Leather Belt",
                    Description = "A simple leather belt.",
                    Category = ItemCategory.Equipment,
                    Slot = EquipmentSlot.Belt,
                    Width = 2,
                    Height = 1,
                    RequiredLevel = 1,
                    MaxSockets = 0,
                    IsTradeable = true,
                    Tags = new HashSet<string> { "accessory", "belt" }
                },
                // Currency
                new BaseType
                {
                    BaseTypeId = "gold_coin",
                    Name = "Gold Coin",
                    Description = "The standard currency.",
                    Category = ItemCategory.Currency,
                    Slot = EquipmentSlot.None,
                    Width = 1,
                    Height = 1,
                    MaxStackSize = 9999,
                    IsTradeable = true,
                    Tags = new HashSet<string> { "currency" }
                }
            },
            Modifiers = new List<ModifierDefinition>
            {
                // Physical Damage
                new ModifierDefinition
                {
                    ModifierId = "added_physical_1",
                    DisplayTemplate = "+{0} to {1} Physical Damage",
                    Type = ModifierType.Prefix,
                    Tier = 1,
                    RequiredItemLevel = 1,
                    Ranges = new[] { new ModifierRange { Min = 1, Max = 5 }, new ModifierRange { Min = 6, Max = 10 } },
                    Weight = 1000,
                    RequiredTags = new HashSet<string> { "weapon" },
                    ModifierGroup = "physical_damage"
                },
                new ModifierDefinition
                {
                    ModifierId = "added_physical_2",
                    DisplayTemplate = "+{0} to {1} Physical Damage",
                    Type = ModifierType.Prefix,
                    Tier = 2,
                    RequiredItemLevel = 15,
                    Ranges = new[] { new ModifierRange { Min = 10, Max = 20 }, new ModifierRange { Min = 21, Max = 35 } },
                    Weight = 800,
                    RequiredTags = new HashSet<string> { "weapon" },
                    ModifierGroup = "physical_damage"
                },
                // Life
                new ModifierDefinition
                {
                    ModifierId = "increased_life_1",
                    DisplayTemplate = "+{0} to Maximum Life",
                    Type = ModifierType.Prefix,
                    Tier = 1,
                    RequiredItemLevel = 1,
                    Ranges = new[] { new ModifierRange { Min = 10, Max = 20 } },
                    Weight = 1000,
                    RequiredTags = new HashSet<string> { "armour" },
                    ModifierGroup = "life"
                },
                // Resistances
                new ModifierDefinition
                {
                    ModifierId = "fire_resistance_1",
                    DisplayTemplate = "+{0}% to Fire Resistance",
                    Type = ModifierType.Suffix,
                    Tier = 1,
                    RequiredItemLevel = 1,
                    Ranges = new[] { new ModifierRange { Min = 10, Max = 20 } },
                    Weight = 1000,
                    ModifierGroup = "fire_resistance"
                },
                new ModifierDefinition
                {
                    ModifierId = "cold_resistance_1",
                    DisplayTemplate = "+{0}% to Cold Resistance",
                    Type = ModifierType.Suffix,
                    Tier = 1,
                    RequiredItemLevel = 1,
                    Ranges = new[] { new ModifierRange { Min = 10, Max = 20 } },
                    Weight = 1000,
                    ModifierGroup = "cold_resistance"
                },
                new ModifierDefinition
                {
                    ModifierId = "lightning_resistance_1",
                    DisplayTemplate = "+{0}% to Lightning Resistance",
                    Type = ModifierType.Suffix,
                    Tier = 1,
                    RequiredItemLevel = 1,
                    Ranges = new[] { new ModifierRange { Min = 10, Max = 20 } },
                    Weight = 1000,
                    ModifierGroup = "lightning_resistance"
                },
                // Attack Speed
                new ModifierDefinition
                {
                    ModifierId = "attack_speed_1",
                    DisplayTemplate = "{0}% Increased Attack Speed",
                    Type = ModifierType.Suffix,
                    Tier = 1,
                    RequiredItemLevel = 1,
                    Ranges = new[] { new ModifierRange { Min = 5, Max = 10 } },
                    Weight = 800,
                    RequiredTags = new HashSet<string> { "weapon" },
                    ModifierGroup = "attack_speed"
                }
            },
            Uniques = new List<UniqueDefinition>()
        };
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
