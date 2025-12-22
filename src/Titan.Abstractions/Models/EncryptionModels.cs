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
    byte[] ClientPublicKey,
    byte[] ClientSigningPublicKey
);

/// <summary>
/// Server's response to key exchange with key identifier.
/// </summary>
public record KeyExchangeResponse(
    string KeyId,
    byte[] ServerPublicKey,
    byte[] ServerSigningPublicKey
);

/// <summary>
/// Server-initiated key rotation request.
/// </summary>
public record KeyRotationRequest(
    string KeyId,
    byte[] ServerPublicKey
);

/// <summary>
/// Client acknowledgment of key rotation with new client public key.
/// </summary>
public record KeyRotationAck(
    byte[] ClientPublicKey
);
