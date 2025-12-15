using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.TestingHost;
using Titan.Abstractions.Grains.Items;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Tests that verify the embedded seed data loads correctly.
/// </summary>
[Collection(ClusterCollection.Name)]
public class SeedDataLoadingTests
{
    private readonly TestCluster _cluster;

    public SeedDataLoadingTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
    }

    [Fact]
    public async Task EmbeddedResource_Exists()
    {
        // Arrange
        var assembly = typeof(Titan.Grains.Hosting.BaseTypeSeedStartupTask).Assembly;
        const string resourceName = "Titan.Grains.Data.item-seed-data.json";

        // Act
        using var stream = assembly.GetManifestResourceStream(resourceName);

        // Assert
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task EmbeddedResource_IsValidJson()
    {
        // Arrange
        var assembly = typeof(Titan.Grains.Hosting.BaseTypeSeedStartupTask).Assembly;
        const string resourceName = "Titan.Grains.Data.item-seed-data.json";

        // Act
        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream!);
        var json = await reader.ReadToEndAsync();

        // Assert - verify it's valid JSON
        Assert.False(string.IsNullOrEmpty(json));
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task EmbeddedResource_HasExpectedStructure()
    {
        // Arrange
        var assembly = typeof(Titan.Grains.Hosting.BaseTypeSeedStartupTask).Assembly;
        const string resourceName = "Titan.Grains.Data.item-seed-data.json";

        // Act
        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream!);
        var json = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(json);

        // Assert - verify expected properties exist
        Assert.True(doc.RootElement.TryGetProperty("BaseTypes", out var baseTypes));
        Assert.True(doc.RootElement.TryGetProperty("Modifiers", out var modifiers));
        Assert.True(doc.RootElement.TryGetProperty("Uniques", out var uniques));
        
        Assert.True(baseTypes.GetArrayLength() > 0);
        Assert.True(modifiers.GetArrayLength() > 0);
    }

    [Fact]
    public async Task EmbeddedResource_ContainsSimpleSword()
    {
        // Arrange
        var assembly = typeof(Titan.Grains.Hosting.BaseTypeSeedStartupTask).Assembly;
        const string resourceName = "Titan.Grains.Data.item-seed-data.json";

        // Act
        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream!);
        var json = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(json);

        // Assert - verify simple_sword exists
        var baseTypes = doc.RootElement.GetProperty("BaseTypes");
        var foundSimpleSword = false;
        foreach (var bt in baseTypes.EnumerateArray())
        {
            if (bt.GetProperty("BaseTypeId").GetString() == "simple_sword")
            {
                foundSimpleSword = true;
                // Verify it has expected properties
                Assert.Equal("Simple Sword", bt.GetProperty("Name").GetString());
                Assert.Equal("MainHand", bt.GetProperty("Slot").GetString());
                break;
            }
        }
        Assert.True(foundSimpleSword, "Expected to find 'simple_sword' base type");
    }

    [Fact]
    public async Task EmbeddedResource_CanDeserialize_ToSeedData()
    {
        // This test verifies the JSON can be deserialized using the same options
        // as BaseTypeSeedHostedService uses at runtime.
        
        // Arrange
        var assembly = typeof(Titan.Grains.Hosting.BaseTypeSeedStartupTask).Assembly;
        const string resourceName = "Titan.Grains.Data.item-seed-data.json";
        
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    static typeInfo =>
                    {
                        if (typeInfo.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object) return;
                        foreach (var prop in typeInfo.Properties)
                        {
                            prop.IsRequired = false;
                        }
                    }
                }
            }
        };

        // Act
        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream!);
        var json = await reader.ReadToEndAsync();
        var seedData = JsonSerializer.Deserialize<Titan.Grains.Hosting.SeedData>(json, options);

        // Assert - deserialization succeeded
        Assert.NotNull(seedData);
        Assert.NotNull(seedData.BaseTypes);
        Assert.NotEmpty(seedData.BaseTypes);
        
        // Verify a specific item deserialized correctly
        var sword = seedData.BaseTypes.FirstOrDefault(bt => bt.BaseTypeId == "simple_sword");
        Assert.NotNull(sword);
        Assert.Equal("Simple Sword", sword.Name);
        Assert.Equal(Titan.Abstractions.Models.Items.EquipmentSlot.MainHand, sword.Slot);
    }
}
