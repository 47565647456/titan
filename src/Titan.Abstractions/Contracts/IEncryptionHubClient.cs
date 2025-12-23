using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for EncryptionHub operations.
/// Used with TypedSignalR.Client source generator.
/// Handles key exchange and rotation for application-layer encryption.
/// </summary>
public interface IEncryptionHubClient
{
    /// <summary>
    /// Get the current encryption configuration from the server.
    /// </summary>
    Task<EncryptionConfig> GetConfig();

    /// <summary>
    /// Perform ECDH key exchange with the server.
    /// </summary>
    /// <param name="request">Client's ECDH and signing public keys</param>
    /// <returns>Server's public keys and key ID</returns>
    Task<KeyExchangeResponse> KeyExchange(KeyExchangeRequest request);

    /// <summary>
    /// Complete a server-initiated key rotation.
    /// </summary>
    /// <param name="ack">Client acknowledgment with new public key</param>
    Task CompleteKeyRotation(KeyRotationAck ack);
}

/// <summary>
/// Receiver interface for encryption hub callbacks from server to client.
/// </summary>
public interface IEncryptionHubReceiver
{
    /// <summary>
    /// Called by server when key rotation is needed.
    /// </summary>
    Task KeyRotation(KeyRotationRequest request);
}
