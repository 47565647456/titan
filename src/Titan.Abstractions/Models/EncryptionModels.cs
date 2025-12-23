using System.Text.Json.Serialization;

namespace Titan.Abstractions.Models;

/// <summary>
/// Server encryption configuration sent to client during login.
/// </summary>
public record EncryptionConfig(
    bool Enabled,
    bool Required
);

/// <summary>
/// Client's public keys for ECDH key exchange and ECDSA signing.
/// </summary>
public record KeyExchangeRequest(
    ReadOnlyMemory<byte> ClientPublicKey,
    ReadOnlyMemory<byte> ClientSigningPublicKey
);

/// <summary>
/// Server's response to key exchange with key identifier.
/// Includes a cryptographically random salt for HKDF key derivation.
/// </summary>
public record KeyExchangeResponse(
    string KeyId,
    ReadOnlyMemory<byte> ServerPublicKey,
    ReadOnlyMemory<byte> ServerSigningPublicKey,
    /// <summary>
    /// Cryptographically random salt for HKDF key derivation (32 bytes for SHA-256).
    /// Both client and server must use this same salt.
    /// </summary>
    ReadOnlyMemory<byte> HkdfSalt,
    /// <summary>
    /// Server's configured grace period in seconds for key rotation.
    /// Clients should use this value instead of hardcoding their own.
    /// </summary>
    int GracePeriodSeconds
);

/// <summary>
/// Server-initiated key rotation request.
/// </summary>
public record KeyRotationRequest(
    [property: JsonPropertyName("newKeyId")]
    string KeyId,
    ReadOnlyMemory<byte> ServerPublicKey,
    /// <summary>
    /// Cryptographically random salt for HKDF key derivation during rotation.
    /// </summary>
    ReadOnlyMemory<byte> HkdfSalt
);

/// <summary>
/// Client acknowledgment of key rotation with new client public keys.
/// Includes signing key to handle cases where multiple instances may have different signing keys.
/// </summary>
public record KeyRotationAck(
    ReadOnlyMemory<byte> ClientPublicKey,
    ReadOnlyMemory<byte> ClientSigningPublicKey
);
