namespace Titan.Abstractions;

/// <summary>
/// Configuration options for base type seeding on application startup.
/// </summary>
public class BaseTypeSeedOptions
{
    public const string SectionName = "BaseTypeSeeding";

    /// <summary>
    /// Path to the JSON seed file. If null, uses embedded defaults.
    /// </summary>
    public string? SeedFilePath { get; set; }

    /// <summary>
    /// If true, re-seeds data even if registry already has entries.
    /// </summary>
    public bool ForceReseed { get; set; } = false;

    /// <summary>
    /// If true, skips seeding if the registry already has entries.
    /// </summary>
    public bool SkipIfPopulated { get; set; } = true;
}
