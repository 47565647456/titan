/**
 * Encryption types for SignalR hub communication.
 * Mirrors the C# types from Titan.Abstractions.Models.
 */

/**
 * Encrypted message envelope containing ciphertext and metadata.
 */
export interface SecureEnvelope {
  keyId: string;
  nonce: string;  // Base64 encoded
  ciphertext: string;  // Base64 encoded
  tag: string;  // Base64 encoded
  signature: string;  // Base64 encoded
  timestamp: number;
  sequenceNumber: number;
}

/**
 * Client's key exchange request sent to EncryptionHub.
 */
export interface KeyExchangeRequest {
  clientPublicKey: string;  // Base64 encoded SPKI format
  clientSigningPublicKey: string;  // Base64 encoded SPKI format
}

/**
 * Server's response to key exchange with its public keys.
 */
export interface KeyExchangeResponse {
  keyId: string;
  serverPublicKey: string;  // Base64 encoded SPKI format
  serverSigningPublicKey: string;  // Base64 encoded SPKI format
  hkdfSalt: string;  // Base64 encoded (32 bytes for SHA-256)
  gracePeriodSeconds: number;
}

/**
 * Encryption configuration from the server.
 */
export interface EncryptionConfig {
  enabled: boolean;
  requireEncryption: boolean;
}

/**
 * Key rotation request from server.
 */
export interface KeyRotationRequest {
  newKeyId: string;
  serverPublicKey: string;  // Base64 encoded
  hkdfSalt: string;  // Base64 encoded (32 bytes for SHA-256)
}

/**
 * Represents an invocation request that is sent inside a SecureEnvelope.
 */
export interface EncryptedInvocation {
  target: string;
  payload: Uint8Array;
}
