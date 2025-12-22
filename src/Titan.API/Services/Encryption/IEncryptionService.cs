using Titan.Abstractions.Models;

namespace Titan.API.Services.Encryption;

/// <summary>
/// Service interface for application-layer payload encryption/decryption.
/// Manages per-user encryption state, key exchange, and key rotation.
/// State is keyed by userId to enable cross-hub encryption.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Gets the encryption configuration for client announcement.
    /// </summary>
    EncryptionConfig GetConfig();

    /// <summary>
    /// Performs ECDH key exchange with a client.
    /// </summary>
    /// <param name="userId">User ID (from JWT)</param>
    /// <param name="clientPublicKey">Client's ECDH public key</param>
    /// <param name="clientSigningPublicKey">Client's ECDSA signing public key</param>
    /// <returns>Server's response with key ID and public keys</returns>
    KeyExchangeResponse PerformKeyExchange(
        string userId,
        byte[] clientPublicKey,
        byte[] clientSigningPublicKey);

    /// <summary>
    /// Performs ECDH key exchange with a client (async wrapper for hub compatibility).
    /// </summary>
    Task<KeyExchangeResponse> PerformKeyExchangeAsync(
        string userId,
        byte[] clientPublicKey,
        byte[] clientSigningPublicKey);

    /// <summary>
    /// Decrypts and verifies a secure envelope from a client.
    /// </summary>
    /// <param name="userId">User ID (from JWT)</param>
    /// <param name="envelope">Encrypted envelope to decrypt</param>
    /// <returns>Decrypted plaintext bytes</returns>
    /// <exception cref="System.Security.SecurityException">If verification or decryption fails</exception>
    Task<byte[]> DecryptAndVerifyAsync(string userId, SecureEnvelope envelope);

    /// <summary>
    /// Decrypts and verifies a secure envelope synchronously (for use in sync protocol contexts).
    /// </summary>
    byte[] DecryptAndVerify(string userId, SecureEnvelope envelope);

    /// <summary>
    /// Encrypts and signs a payload for a client.
    /// </summary>
    /// <param name="userId">User ID (from JWT)</param>
    /// <param name="plaintext">Plaintext bytes to encrypt</param>
    /// <param name="keyId">Optional key ID to use (if it's a valid current/previous key)</param>
    /// <returns>Encrypted and signed envelope</returns>
    Task<SecureEnvelope> EncryptAndSignAsync(string userId, byte[] plaintext, string? keyId = null);

    /// <summary>
    /// Encrypts and signs a payload synchronously (for use in sync protocol contexts).
    /// </summary>
    /// <param name="userId">User ID (from JWT)</param>
    /// <param name="plaintext">Plaintext bytes to encrypt</param>
    /// <param name="keyId">Optional key ID to use (if it's a valid current/previous key)</param>
    SecureEnvelope EncryptAndSign(string userId, byte[] plaintext, string? keyId = null);

    /// <summary>
    /// Initiates key rotation for a user.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>Key rotation request with new server public key</returns>
    KeyRotationRequest InitiateKeyRotation(string userId);

    /// <summary>
    /// Initiates key rotation for a user (async wrapper for hub compatibility).
    /// </summary>
    Task<KeyRotationRequest> InitiateKeyRotationAsync(string userId);

    /// <summary>
    /// Completes key rotation after receiving client acknowledgment.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="ack">Client's acknowledgment with new public key</param>
    void CompleteKeyRotation(string userId, KeyRotationAck ack);

    /// <summary>
    /// Completes key rotation (async wrapper for hub compatibility).
    /// </summary>
    Task CompleteKeyRotationAsync(string userId, KeyRotationAck ack);

    /// <summary>
    /// Checks if a user has encryption enabled.
    /// </summary>
    bool IsEncryptionEnabled(string userId);

    /// <summary>
    /// Checks if a user needs key rotation.
    /// </summary>
    bool NeedsKeyRotation(string userId);

    /// <summary>
    /// Gets statistics for a user's encryption state.
    /// </summary>
    ConnectionEncryptionStats? GetConnectionStats(string userId);

    /// <summary>
    /// Cleans up encryption state for a user (e.g., when session expires).
    /// </summary>
    void RemoveConnection(string userId);

    /// <summary>
    /// Gets all users that need key rotation.
    /// </summary>
    IEnumerable<string> GetConnectionsNeedingRotation();

    /// <summary>
    /// Gets all user IDs that have encryption enabled.
    /// </summary>
    IEnumerable<string> GetAllEncryptedUserIds();

    /// <summary>
    /// Cleans up expired previous keys from all connections.
    /// Should be called periodically by a background service.
    /// Returns the number of keys that were cleaned up.
    /// </summary>
    int CleanupExpiredPreviousKeys();

    /// <summary>
    /// Gets the current encryption metrics snapshot.
    /// </summary>
    EncryptionMetricsSnapshot GetMetrics();

    /// <summary>
    /// Sets encryption enabled state at runtime (for testing).
    /// </summary>
    /// <param name="enabled">Whether encryption should be enabled</param>
    void SetEnabled(bool enabled);

    /// <summary>
    /// Sets encryption required state at runtime (for testing).
    /// </summary>
    /// <param name="required">Whether encryption should be required</param>
    void SetRequired(bool required);
}

/// <summary>
/// Statistics about a user's encryption state.
/// </summary>
public record ConnectionEncryptionStats(
    string KeyId,
    long MessageCount,
    DateTimeOffset KeyCreatedAt,
    DateTimeOffset? LastActivityAt
);
