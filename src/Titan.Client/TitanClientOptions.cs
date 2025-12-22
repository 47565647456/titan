namespace Titan.Client;

/// <summary>
/// Configuration options for TitanClient.
/// </summary>
public class TitanClientOptions
{
    /// <summary>
    /// The base URL of the Titan API (e.g., "https://api.titan.gg").
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional logger factory for client logging.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Connection timeout for SignalR hub connections.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to enable automatic reconnection for SignalR hubs.
    /// </summary>
    public bool EnableAutoReconnect { get; set; } = true;

    /// <summary>
    /// Whether to enable application-layer payload encryption.
    /// When enabled, the client will perform key exchange and encrypt all hub messages.
    /// Server must also have encryption enabled.
    /// </summary>
    public bool EnablePayloadEncryption { get; set; } = false;
}
