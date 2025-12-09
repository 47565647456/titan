namespace Titan.Abstractions;

/// <summary>
/// Configuration options for the item type registry.
/// Can be configured via appsettings.json under "ItemRegistry" section.
/// </summary>
public class ItemRegistryOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "ItemRegistry";

    /// <summary>
    /// Path to JSON file containing item type definitions for seeding.
    /// Leave empty or null to skip seeding.
    /// </summary>
    public string? SeedFilePath { get; set; }

    /// <summary>
    /// If true, re-seed even if registry already has data (useful for dev/testing).
    /// Default: false
    /// </summary>
    public bool ForceSeed { get; set; } = false;

    /// <summary>
    /// If true, allow adding items with unregistered types (uses defaults: MaxStackSize=1, IsTradeable=true).
    /// If false, reject items with unknown types.
    /// Default: true (permissive for development)
    /// </summary>
    public bool AllowUnknownItemTypes { get; set; } = true;
}
