namespace Titan.Abstractions;

/// <summary>
/// Configuration options for trading behavior.
/// Can be configured via appsettings.json under "Trading" section.
/// </summary>
public class TradingOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Trading";

    /// <summary>
    /// How long a trade can remain in Pending status before auto-expiring.
    /// Set to TimeSpan.Zero to disable expiration.
    /// Default: 15 minutes.
    /// </summary>
    public TimeSpan TradeTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often to check for expired trades (grain timer interval).
    /// Default: 1 minute.
    /// </summary>
    public TimeSpan ExpirationCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
}
