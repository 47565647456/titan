using MemoryPack;

namespace Titan.Abstractions.Models;

/// <summary>
/// Encrypted message envelope for application-layer payload encryption.
/// Contains ciphertext, authentication tag, signature, and metadata for replay protection.
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial record SecureEnvelope
{
    /// <summary>
    /// Key identifier - indicates which session key was used for encryption.
    /// Used during key rotation to support in-flight messages with old keys.
    /// </summary>
    [Id(0), MemoryPackOrder(0)]
    public required string KeyId { get; init; }

    /// <summary>
    /// 12-byte nonce for AES-GCM. Uses deterministic construction:
    /// [4 bytes: connection hash][8 bytes: monotonic counter]
    /// </summary>
    [Id(1), MemoryPackOrder(1)]
    public required byte[] Nonce { get; init; }

    /// <summary>
    /// Encrypted payload bytes.
    /// </summary>
    [Id(2), MemoryPackOrder(2)]
    public required byte[] Ciphertext { get; init; }

    /// <summary>
    /// 16-byte GCM authentication tag for integrity verification.
    /// </summary>
    [Id(3), MemoryPackOrder(3)]
    public required byte[] Tag { get; init; }

    /// <summary>
    /// ECDSA signature over (KeyId || Nonce || Ciphertext || Tag || Timestamp || SequenceNumber).
    /// Provides authenticity and non-repudiation.
    /// </summary>
    [Id(4), MemoryPackOrder(4)]
    public required byte[] Signature { get; init; }

    /// <summary>
    /// Unix timestamp in milliseconds when message was created.
    /// Used for replay protection with time window validation.
    /// </summary>
    [Id(5), MemoryPackOrder(5)]
    public required long Timestamp { get; init; }

    /// <summary>
    /// Monotonically increasing sequence number per connection.
    /// Used for gap detection and replay protection.
    /// </summary>
    [Id(6), MemoryPackOrder(6)]
    public required long SequenceNumber { get; init; }
}
