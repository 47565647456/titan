namespace Titan.Abstractions;

/// <summary>
/// Configuration options for item registry caching (BaseTypeReaderGrain, ModifierReaderGrain, etc.)
/// </summary>
public class ItemRegistryCacheOptions
{
    /// <summary>
    /// How long to cache registry data before refreshing.
    /// Set to TimeSpan.Zero to disable caching (useful for testing).
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}
