/**
 * Encryption module exports.
 * Provides client-side encryption for SignalR hub communication.
 */

export { Encryptor } from './encryptor';
export { EncryptedSignalRConnection, createEncryptedConnection } from './signalREncrypted';
export type { EncryptedConnectionOptions } from './signalREncrypted';
export type {
  SecureEnvelope,
  KeyExchangeRequest,
  KeyExchangeResponse,
  EncryptionConfig,
  KeyRotationRequest,
} from './types';
