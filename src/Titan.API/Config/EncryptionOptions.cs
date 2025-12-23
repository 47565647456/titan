using System.ComponentModel.DataAnnotations;

namespace Titan.API.Config;

/// <summary>
/// Configuration options for application-layer payload encryption.
/// </summary>
public class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>
    /// Whether encryption is enabled. When enabled, clients can negotiate encryption.
    /// Default: false (disabled for development)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Whether encryption is required. If true, unencrypted clients are rejected.
    /// Default: false (allow unencrypted clients during transition period)
    /// </summary>
    public bool RequireEncryption { get; set; } = false;

    /// <summary>
    /// Key rotation interval in minutes. Keys are rotated after this time.
    /// Default: 30 minutes
    /// </summary>
    [Range(1, 1440)]
    public int KeyRotationIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum messages per key before rotation is triggered.
    /// Well under AES-GCM's 2^32 limit for random nonces.
    /// Default: 1,000,000 messages
    /// </summary>
    [Range(1000, 100_000_000)]
    public int MaxMessagesPerKey { get; set; } = 1_000_000;

    /// <summary>
    /// Replay protection time window in seconds.
    /// Messages older than this are rejected.
    /// Default: 60 seconds
    /// </summary>
    [Range(5, 300)]
    public int ReplayWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Grace period in seconds during key rotation.
    /// Old keys remain valid for this duration to handle in-flight messages.
    /// Default: 30 seconds
    /// </summary>
    [Range(5, 120)]
    public int KeyRotationGracePeriodSeconds { get; set; } = 30;

    /// <summary>
    /// How often the background service checks for keys needing rotation.
    /// Default: 30 seconds
    /// </summary>
    [Range(5, 300)]
    public int KeyRotationCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum concurrent sends during group broadcasts.
    /// Higher values increase throughput but consume more resources.
    /// Default: 50
    /// </summary>
    [Range(1, 500)]
    public int BroadcastMaxConcurrency { get; set; } = 50;

    /// <summary>
    /// How long to persist encryption state in Redis before expiry.
    /// If user doesn't reconnect within this time, state expires and they must re-negotiate.
    /// Default: 24 hours
    /// </summary>
    [Range(1, 168)] // 1 hour to 1 week
    public int StateExpiryHours { get; set; } = 24;
}
