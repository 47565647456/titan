using Titan.Abstractions.Models;

namespace Titan.Client.Encryption;

/// <summary>
/// Client-side encryption interface for payload encryption/decryption.
/// Manages ECDH key exchange and AES-GCM encryption with ECDSA signing.
/// </summary>
public interface IClientEncryptor : IDisposable
{
    /// <summary>
    /// Performs ECDH key exchange with the server.
    /// </summary>
    /// <param name="keyExchangeFunc">Function to send key exchange request to server</param>
    /// <returns>True if key exchange was successful</returns>
    Task<bool> PerformKeyExchangeAsync(Func<KeyExchangeRequest, Task<KeyExchangeResponse>> keyExchangeFunc);

    /// <summary>
    /// Encrypts and signs a payload.
    /// </summary>
    /// <param name="plaintext">Plaintext bytes to encrypt</param>
    /// <returns>Encrypted and signed envelope</returns>
    SecureEnvelope EncryptAndSign(byte[] plaintext);

    /// <summary>
    /// Decrypts and verifies a payload from the server.
    /// </summary>
    /// <param name="envelope">Encrypted envelope to decrypt</param>
    /// <returns>Decrypted plaintext bytes</returns>
    /// <exception cref="System.Security.SecurityException">If verification or decryption fails</exception>
    byte[] DecryptAndVerify(SecureEnvelope envelope);

    /// <summary>
    /// Handles a key rotation request from the server.
    /// </summary>
    /// <param name="request">Key rotation request with new server public key</param>
    /// <returns>Acknowledgment with new client public key</returns>
    KeyRotationAck HandleRotationRequest(KeyRotationRequest request);

    /// <summary>
    /// Whether the encryptor has completed key exchange.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Current key ID being used.
    /// </summary>
    string? CurrentKeyId { get; }

    /// <summary>
    /// Previous key ID (if in rotation grace period).
    /// </summary>
    string? PreviousKeyId { get; }
}
