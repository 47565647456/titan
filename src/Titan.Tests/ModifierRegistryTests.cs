using Orleans.TestingHost;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Unit tests for ModifierRegistryGrain and ModifierReaderGrain.
/// </summary>
[Collection(ClusterCollection.Name)]
public class ModifierRegistryTests
{
    private readonly TestCluster _cluster;

    public ModifierRegistryTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
    }

    [Fact]
    public async Task RegisterAsync_AddsModifier()
    {
        // Arrange
        var modifierId = $"test_mod_{Guid.NewGuid():N}";
        var registry = _cluster.GrainFactory.GetGrain<IModifierRegistryGrain>("default");
        var modifier = new ModifierDefinition
        {
            ModifierId = modifierId,
            DisplayTemplate = "+{0} Test",
            Type = ModifierType.Prefix,
            Tier = 1,
            RequiredItemLevel = 1,
            Ranges = new[] { new ModifierRange { Min = 1, Max = 10 } },
            Weight = 1000,
            ModifierGroup = "test_group"
        };

        // Act
        await registry.RegisterAsync(modifier);

        // Assert
        var retrieved = await registry.GetAsync(modifierId);
        Assert.NotNull(retrieved);
        Assert.Equal(modifierId, retrieved.ModifierId);
    }

    [Fact]
    public async Task Reader_RollModifierAsync_ReturnsValidRoll()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<IModifierRegistryGrain>("default");
        var reader = _cluster.GrainFactory.GetGrain<IModifierReaderGrain>("default");
        
        var modId = $"roll_test_{Guid.NewGuid():N}";
        var modifier = new ModifierDefinition
        {
            ModifierId = modId,
            DisplayTemplate = "+{0} to Maximum Life",
            Type = ModifierType.Prefix,
            Tier = 1,
            RequiredItemLevel = 1,
            Ranges = new[] { new ModifierRange { Min = 10, Max = 50 } },
            Weight = 1000,
            ModifierGroup = "test_life"
        };
        await registry.RegisterAsync(modifier);

        // Act
        var rolled = await reader.RollModifierAsync(modId);

        // Assert
        Assert.NotNull(rolled);
        Assert.Equal(modId, rolled.ModifierId);
        Assert.Single(rolled.Values);
        Assert.InRange(rolled.Values[0], 10, 50);
    }
}
