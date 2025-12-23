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
    /// Performs ECDH key exchange with a client to establish shared encryption keys.
    /// If the user already has encryption state, it will be overwritten with the new keys.
    /// </summary>
    /// <param name="userId">User ID (from session ticket). Must not be null or empty.</param>
    /// <param name="clientPublicKey">Client's ECDH public key in SubjectPublicKeyInfo (DER) format, using P-256 curve.</param>
    /// <param name="clientSigningPublicKey">Client's ECDSA signing public key in SubjectPublicKeyInfo (DER) format, using P-256 curve.</param>
    /// <returns>Key exchange response containing server's public key, key ID, HKDF salt, and grace period configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId, clientPublicKey, or clientSigningPublicKey is null.</exception>
    /// <exception cref="ArgumentException">Thrown when userId is empty, or when public keys are invalid format/length.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">Thrown when key import or derivation fails.</exception>
    Task<KeyExchangeResponse> PerformKeyExchangeAsync(
        string userId,
        byte[] clientPublicKey,
        byte[] clientSigningPublicKey);

    /// <summary>
    /// Decrypts and verifies a secure envelope from a client.
    /// </summary>
    /// <param name="userId">User ID (from session ticket). Must not be null or empty.</param>
    /// <param name="envelope">Encrypted envelope to decrypt. Must not be null.</param>
    /// <returns>Decrypted plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or envelope is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when user has no encryption state (key exchange not completed).</exception>
    /// <exception cref="System.Security.SecurityException">Thrown when verification fails (invalid signature, replay attack, or decryption error).</exception>
    Task<byte[]> DecryptAndVerifyAsync(string userId, SecureEnvelope envelope);

    /// <summary>
    /// Decrypts and verifies a secure envelope synchronously (for use in sync protocol contexts).
    /// </summary>
    /// <param name="userId">User ID (from session ticket). Must not be null or empty.</param>
    /// <param name="envelope">Encrypted envelope to decrypt. Must not be null.</param>
    /// <returns>Decrypted plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or envelope is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when user has no encryption state (key exchange not completed).</exception>
    /// <exception cref="System.Security.SecurityException">Thrown when verification fails (invalid signature, replay attack, or decryption error).</exception>
    byte[] DecryptAndVerify(string userId, SecureEnvelope envelope);

    /// <summary>
    /// Encrypts and signs a payload for a client.
    /// </summary>
    /// <param name="userId">User ID (from session ticket). Must not be null or empty.</param>
    /// <param name="plaintext">Plaintext bytes to encrypt. Must not be null (may be empty).</param>
    /// <param name="keyId">Optional key ID to use. If null, uses the current active key. If specified, must be a valid current or previous key.</param>
    /// <returns>Encrypted and signed envelope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or plaintext is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when user has no encryption state (key exchange not completed).</exception>
    /// <exception cref="ArgumentException">Thrown when keyId is specified but is not a valid current or previous key.</exception>
    Task<SecureEnvelope> EncryptAndSignAsync(string userId, byte[] plaintext, string? keyId = null);

    /// <summary>
    /// Encrypts and signs a payload synchronously (for use in sync protocol contexts).
    /// </summary>
    /// <param name="userId">User ID (from session ticket). Must not be null or empty.</param>
    /// <param name="plaintext">Plaintext bytes to encrypt. Must not be null (may be empty).</param>
    /// <param name="keyId">Optional key ID to use. If null, uses the current active key. If specified, must be a valid current or previous key.</param>
    /// <returns>Encrypted and signed envelope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or plaintext is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when user has no encryption state (key exchange not completed).</exception>
    /// <exception cref="ArgumentException">Thrown when keyId is specified but is not a valid current or previous key.</exception>
    SecureEnvelope EncryptAndSign(string userId, byte[] plaintext, string? keyId = null);

    /// <summary>
    /// Initiates key rotation for a user. Generates a new server keypair and marks rotation as pending.
    /// The previous key remains valid during the grace period until CompleteKeyRotation is called.
    /// If rotation is already in progress, this returns the existing pending rotation request.
    /// </summary>
    /// <param name="userId">User ID. Must not be null or empty.</param>
    /// <returns>Key rotation request with new server public key and HKDF salt.</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when user has no encryption state (key exchange not completed).</exception>
    KeyRotationRequest InitiateKeyRotation(string userId);

    /// <summary>
    /// Initiates key rotation for a user (async wrapper for hub compatibility).
    /// See <see cref="InitiateKeyRotation"/> for detailed behavior.
    /// </summary>
    /// <param name="userId">User ID. Must not be null or empty.</param>
    /// <returns>Key rotation request with new server public key and HKDF salt.</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when user has no encryption state (key exchange not completed).</exception>
    Task<KeyRotationRequest> InitiateKeyRotationAsync(string userId);

    /// <summary>
    /// Completes key rotation after receiving client acknowledgment. 
    /// The pending key becomes active and the old key moves to previous (with grace period expiry).
    /// </summary>
    /// <param name="userId">User ID. Must not be null or empty.</param>
    /// <param name="ack">Client's acknowledgment containing new client public key and signing key.</param>
    /// <exception cref="ArgumentNullException">Thrown when userId or ack is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no rotation is pending (InitiateKeyRotation was not called first).</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">Thrown when client key import fails.</exception>
    void CompleteKeyRotation(string userId, KeyRotationAck ack);

    /// <summary>
    /// Completes key rotation (async wrapper for hub compatibility).
    /// See <see cref="CompleteKeyRotation"/> for detailed behavior.
    /// </summary>
    /// <param name="userId">User ID. Must not be null or empty.</param>
    /// <param name="ack">Client's acknowledgment containing new client public key and signing key.</param>
    /// <exception cref="ArgumentNullException">Thrown when userId or ack is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no rotation is pending (InitiateKeyRotation was not called first).</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">Thrown when client key import fails.</exception>
    Task CompleteKeyRotationAsync(string userId, KeyRotationAck ack);

    /// <summary>
    /// Checks if a user has encryption enabled (has completed key exchange).
    /// </summary>
    /// <param name="userId">User ID to check.</param>
    /// <returns>True if the user has encryption state; false otherwise.</returns>
    /// <remarks>When this returns false, <see cref="GetConnectionStats"/> will return null for the same userId.</remarks>
    bool IsEncryptionEnabled(string userId);

    /// <summary>
    /// Checks if a user needs key rotation based on message count or key age thresholds.
    /// </summary>
    /// <param name="userId">User ID to check.</param>
    /// <returns>True if rotation is recommended; false if no encryption state or rotation not needed.</returns>
    bool NeedsKeyRotation(string userId);

    /// <summary>
    /// Gets statistics for a user's encryption state.
    /// </summary>
    /// <param name="userId">User ID to get stats for.</param>
    /// <returns>Connection stats if user has encryption state; null if no encryption state exists.</returns>
    /// <remarks>Returns null when <see cref="IsEncryptionEnabled"/> returns false for the same userId.</remarks>
    ConnectionEncryptionStats? GetConnectionStats(string userId);

    /// <summary>
    /// Cleans up encryption state for a user (e.g., when session expires).
    /// </summary>
    /// <param name="userId">User ID to remove.</param>
    /// <returns>True if the user had encryption state that was removed; false if no state existed.</returns>
    bool RemoveConnection(string userId);

    /// <summary>
    /// Gets all users that need key rotation based on message count or key age thresholds.
    /// </summary>
    /// <returns>Enumerable of user IDs needing rotation.</returns>
    /// <remarks>
    /// <b>Performance note:</b> This method enumerates all encrypted users and may be expensive on large datasets.
    /// Consider using with limits or in background processing only.
    /// </remarks>
    IEnumerable<string> GetConnectionsNeedingRotation();

    /// <summary>
    /// Gets all user IDs that have encryption enabled.
    /// </summary>
    /// <returns>Enumerable of all encrypted user IDs.</returns>
    /// <remarks>
    /// <b>Performance note:</b> This method enumerates all encrypted users and may be expensive on large datasets.
    /// Consider using with limits or in background processing only.
    /// </remarks>
    IEnumerable<string> GetAllEncryptedUserIds();

    /// <summary>
    /// Cleans up expired previous keys from all connections.
    /// Intended for periodic background use to free memory from old rotation keys.
    /// </summary>
    /// <returns>The number of expired keys that were cleaned up.</returns>
    int CleanupExpiredPreviousKeys();

    /// <summary>
    /// Gets the current encryption metrics snapshot.
    /// </summary>
    EncryptionMetricsSnapshot GetMetrics();

    /// <summary>
    /// Sets encryption enabled state at runtime.
    /// WARNING: Admin-only operation. Must be protected by authorization at the controller/endpoint level.
    /// </summary>
    /// <param name="enabled">Whether encryption should be enabled</param>
    void SetEnabled(bool enabled);

    /// <summary>
    /// Sets encryption required state at runtime.
    /// WARNING: Admin-only operation. Must be protected by authorization at the controller/endpoint level.
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
