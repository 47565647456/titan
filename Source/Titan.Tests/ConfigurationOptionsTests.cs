namespace Titan.Tests;

/// <summary>
/// Tests for configuration option classes to ensure proper defaults
/// and that all properties are exercised for coverage.
/// </summary>
public class ConfigurationOptionsTests
{
    #region BaseTypeSeedOptions Tests

    [Fact]
    public void BaseTypeSeedOptions_SectionName_IsCorrect()
    {
        Assert.Equal("BaseTypeSeeding", Titan.Abstractions.BaseTypeSeedOptions.SectionName);
    }

    [Fact]
    public void BaseTypeSeedOptions_DefaultValues_AreCorrect()
    {
        var options = new Titan.Abstractions.BaseTypeSeedOptions();

        Assert.Null(options.SeedFilePath);
        Assert.False(options.ForceReseed);
        Assert.True(options.SkipIfPopulated);
    }

    [Fact]
    public void BaseTypeSeedOptions_SeedFilePath_CanBeSet()
    {
        var options = new Titan.Abstractions.BaseTypeSeedOptions
        {
            SeedFilePath = "/path/to/seeds.json"
        };

        Assert.Equal("/path/to/seeds.json", options.SeedFilePath);
    }

    [Fact]
    public void BaseTypeSeedOptions_ForceReseed_CanBeSet()
    {
        var options = new Titan.Abstractions.BaseTypeSeedOptions
        {
            ForceReseed = true
        };

        Assert.True(options.ForceReseed);
    }

    [Fact]
    public void BaseTypeSeedOptions_SkipIfPopulated_CanBeSet()
    {
        var options = new Titan.Abstractions.BaseTypeSeedOptions
        {
            SkipIfPopulated = false
        };

        Assert.False(options.SkipIfPopulated);
    }

    #endregion

    #region ItemRegistryOptions Tests

    [Fact]
    public void ItemRegistryOptions_SectionName_IsCorrect()
    {
        Assert.Equal("ItemRegistry", Titan.Abstractions.ItemRegistryOptions.SectionName);
    }

    [Fact]
    public void ItemRegistryOptions_DefaultValues_AreCorrect()
    {
        var options = new Titan.Abstractions.ItemRegistryOptions();

        Assert.Null(options.SeedFilePath);
        Assert.False(options.ForceSeed);
        Assert.True(options.AllowUnknownItemTypes);
    }

    [Fact]
    public void ItemRegistryOptions_SeedFilePath_CanBeSet()
    {
        var options = new Titan.Abstractions.ItemRegistryOptions
        {
            SeedFilePath = "/path/to/item-types.json"
        };

        Assert.Equal("/path/to/item-types.json", options.SeedFilePath);
    }

    [Fact]
    public void ItemRegistryOptions_ForceSeed_CanBeSet()
    {
        var options = new Titan.Abstractions.ItemRegistryOptions
        {
            ForceSeed = true
        };

        Assert.True(options.ForceSeed);
    }

    [Fact]
    public void ItemRegistryOptions_AllowUnknownItemTypes_CanBeSet()
    {
        var options = new Titan.Abstractions.ItemRegistryOptions
        {
            AllowUnknownItemTypes = false
        };

        Assert.False(options.AllowUnknownItemTypes);
    }

    #endregion
}
