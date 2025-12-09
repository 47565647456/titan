namespace Titan.Abstractions;

/// <summary>
/// Constants for Orleans Stream configuration.
/// </summary>
public static class TradeStreamConstants
{
    /// <summary>
    /// The Orleans Stream provider name for trade events.
    /// </summary>
    public const string ProviderName = "TradeEvents";

    /// <summary>
    /// The stream namespace for trade events.
    /// Stream ID = (Namespace, TradeId)
    /// </summary>
    public const string Namespace = "trade-events";
}
