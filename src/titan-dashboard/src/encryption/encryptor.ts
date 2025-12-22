/**
 * Client-side encryption module for SignalR hub communication.
 * Uses Web Crypto API for ECDH key exchange, ECDSA signing, and AES-256-GCM encryption.
 * 
 * Mirrors the C# ClientEncryptor from Titan.Client.Encryption.
 */

import type { SecureEnvelope, KeyExchangeRequest, KeyExchangeResponse, KeyRotationRequest } from './types';
import { arrayBufferToBase64, base64ToArrayBuffer } from './utils';

// Named curve for ECDH and ECDSA
const CURVE = 'P-256';
const AES_KEY_LENGTH = 256;
const NONCE_LENGTH = 12;
const GCM_TAG_LENGTH = 128; // Bits
const REPLAY_WINDOW_SECONDS = 60;
const CLOCK_SKEW_SECONDS = 5;
const MAX_SEQUENCE_GAP = 100; // Small buffer for in-flight messages on refresh



/**
 * Encryption client for SignalR hub communication.
 * Handles key exchange, encryption, decryption, and signing.
 * Supports key rotation grace periods and session persistence.
 */
export class Encryptor {
  private ecdhKeyPair: CryptoKeyPair | null = null;
  // We only need the private key for signing - publicKey is null after restoration from session
  private signingPrivateKey: CryptoKey | null = null;
  private aesKey: CryptoKey | null = null;
  private serverSigningPublicKey: CryptoKey | null = null;
  private keyId: string | null = null;
  private sequenceNumber = 0;
  private lastReceivedSequencePerKey: Map<string, number> = new Map();

  // Grace period / Previous key state
  private previousKeyId: string | null = null;
  private previousAesKey: CryptoKey | null = null;
  private previousServerSigningPublicKey: CryptoKey | null = null;
  private previousKeyExpiresAt: number | null = null;  // Unix timestamp (ms)
  
  /** Grace period duration in milliseconds (should match server) */
  private static readonly GRACE_PERIOD_MS = 300 * 1000;  // 5 minutes

  private readonly STORAGE_KEY = 'titan_encryption_session';

  /**
   * Whether encryption is initialized and ready to use.
   */
  get isInitialized(): boolean {
    return this.aesKey !== null && this.keyId !== null;
  }

  /**
   * Current encryption key ID.
   */
  get currentKeyId(): string | null {
    return this.keyId;
  }

  /**
   * Generate key pairs and create a key exchange request.
   */
  async createKeyExchangeRequest(): Promise<KeyExchangeRequest> {
    // Generate ECDH key pair for key exchange
    this.ecdhKeyPair = await crypto.subtle.generateKey(
      { name: 'ECDH', namedCurve: CURVE },
      true,
      ['deriveBits']
    );

    // Generate ECDSA key pair for signing
    const signingKeyPair = await crypto.subtle.generateKey(
      { name: 'ECDSA', namedCurve: CURVE },
      true,
      ['sign', 'verify']
    );
    this.signingPrivateKey = signingKeyPair.privateKey;

    // Export public keys in SPKI format
    const ecdhPublicKey = await crypto.subtle.exportKey('spki', this.ecdhKeyPair.publicKey);
    const signingPublicKey = await crypto.subtle.exportKey('spki', signingKeyPair.publicKey);

    return {
      clientPublicKey: arrayBufferToBase64(ecdhPublicKey),
      clientSigningPublicKey: arrayBufferToBase64(signingPublicKey),
    };
  }

  /**
   * Complete key exchange using server's response.
   */
  async completeKeyExchange(response: KeyExchangeResponse): Promise<void> {
    if (!this.ecdhKeyPair) {
      throw new Error('Key exchange not initiated. Call createKeyExchangeRequest first.');
    }

    // Import server's ECDH public key
    const serverPublicKeyBytes = base64ToArrayBuffer(response.serverPublicKey);
    const serverEcdhPublicKey = await crypto.subtle.importKey(
      'spki',
      serverPublicKeyBytes.buffer as ArrayBuffer,
      { name: 'ECDH', namedCurve: CURVE },
      false,
      []
    );

    // Derive shared secret
    const sharedSecret = await crypto.subtle.deriveBits(
      { name: 'ECDH', public: serverEcdhPublicKey },
      this.ecdhKeyPair.privateKey,
      256
    );
    
    // Derive AES key from shared secret using HKDF with salt from server
    const sharedSecretKey = await crypto.subtle.importKey(
      'raw',
      sharedSecret,
      { name: 'HKDF' },
      false,
      ['deriveKey']
    );

    // Decode the salt from server response
    const hkdfSaltBytes = base64ToArrayBuffer(response.hkdfSalt);

    const newAesKey = await crypto.subtle.deriveKey(
      {
        name: 'HKDF',
        hash: 'SHA-256',
        salt: hkdfSaltBytes.buffer.slice(hkdfSaltBytes.byteOffset, hkdfSaltBytes.byteOffset + hkdfSaltBytes.byteLength) as ArrayBuffer,
        info: new TextEncoder().encode('titan-encryption-key'),
      },
      sharedSecretKey,
      { name: 'AES-GCM', length: AES_KEY_LENGTH },
      true,
      ['encrypt', 'decrypt']
    );

    // Import server's new signing public key
    const serverSigningKeyBytes = base64ToArrayBuffer(response.serverSigningPublicKey);
    const newServerSigningKey = await crypto.subtle.importKey(
      'spki',
      serverSigningKeyBytes.buffer as ArrayBuffer,
      { name: 'ECDSA', namedCurve: CURVE },
      true,
      ['verify']
    );

    // Rotation: Move current keys to previous with expiry tracking
    if (this.keyId && this.aesKey && this.serverSigningPublicKey) {
      this.previousKeyId = this.keyId;
      this.previousAesKey = this.aesKey;
      this.previousServerSigningPublicKey = this.serverSigningPublicKey;
      this.previousKeyExpiresAt = Date.now() + Encryptor.GRACE_PERIOD_MS;
    }

    // Set new keys
    this.aesKey = newAesKey;
    this.serverSigningPublicKey = newServerSigningKey;
    this.keyId = response.keyId;
    this.sequenceNumber = 0; // Reset sequence for new key

    // Persist immediately
    await this.saveSession();
  }

  /**
   * Handle a key rotation request from the server.
   * Generates new ECDH keypair and derives new AES key.
   * Returns the client's new public key for the server.
   */
  async handleKeyRotation(request: KeyRotationRequest): Promise<{ clientPublicKey: string }> {
    if (!this.aesKey || !this.keyId) {
      throw new Error('Cannot rotate keys: encryption not initialized');
    }

    // Move current keys to previous for grace period
    this.previousKeyId = this.keyId;
    this.previousAesKey = this.aesKey;
    this.previousServerSigningPublicKey = this.serverSigningPublicKey;
    this.previousKeyExpiresAt = Date.now() + Encryptor.GRACE_PERIOD_MS;

    // Generate new ECDH keypair
    this.ecdhKeyPair = await crypto.subtle.generateKey(
      { name: 'ECDH', namedCurve: CURVE },
      true,
      ['deriveBits']
    );

    // Export client public key
    const clientPublicKeySpki = await crypto.subtle.exportKey('spki', this.ecdhKeyPair.publicKey);

    // Import server's new public key
    const serverPublicKeyBytes = base64ToArrayBuffer(request.serverPublicKey);
    const serverEcdhPublicKey = await crypto.subtle.importKey(
      'spki',
      serverPublicKeyBytes.buffer.slice(serverPublicKeyBytes.byteOffset, serverPublicKeyBytes.byteOffset + serverPublicKeyBytes.byteLength) as ArrayBuffer,
      { name: 'ECDH', namedCurve: CURVE },
      false,
      []
    );

    // Derive new shared secret
    const sharedSecret = await crypto.subtle.deriveBits(
      { name: 'ECDH', public: serverEcdhPublicKey },
      this.ecdhKeyPair.privateKey,
      256
    );

    // Import shared secret for HKDF
    const sharedSecretKey = await crypto.subtle.importKey(
      'raw',
      sharedSecret,
      { name: 'HKDF' },
      false,
      ['deriveKey']
    );

    // Decode the salt from request
    const hkdfSaltBytes = base64ToArrayBuffer(request.hkdfSalt);

    // Derive new AES key using salt from request
    this.aesKey = await crypto.subtle.deriveKey(
      {
        name: 'HKDF',
        hash: 'SHA-256',
        salt: hkdfSaltBytes.buffer.slice(hkdfSaltBytes.byteOffset, hkdfSaltBytes.byteOffset + hkdfSaltBytes.byteLength) as ArrayBuffer,
        info: new TextEncoder().encode('titan-encryption-key'),
      },
      sharedSecretKey,
      { name: 'AES-GCM', length: AES_KEY_LENGTH },
      true,
      ['encrypt', 'decrypt']
    );

    // Update key ID and reset sequence
    this.keyId = request.newKeyId;
    this.sequenceNumber = 0;

    // Persist the updated state
    await this.saveSession();

    console.log('[Encryptor] Key rotation completed, new keyId:', this.keyId);

    return {
      clientPublicKey: arrayBufferToBase64(clientPublicKeySpki),
    };
  }

  /**
   * Encrypt and sign data, returning a SecureEnvelope.
   * Always uses the CURRENT key.
   */
  async encryptAndSign(plaintext: Uint8Array): Promise<SecureEnvelope> {
    if (!this.aesKey || !this.signingPrivateKey || !this.keyId) {
      throw new Error('Encryption not initialized. Complete key exchange first.');
    }

    // Generate nonce (12 bytes)
    const nonce = new Uint8Array(NONCE_LENGTH);
    crypto.getRandomValues(nonce);

    // Encrypt with AES-GCM - slice to get proper ArrayBuffer
    const ciphertextWithTag = await crypto.subtle.encrypt(
      { name: 'AES-GCM', iv: nonce, tagLength: GCM_TAG_LENGTH },
      this.aesKey,
      plaintext.buffer.slice(plaintext.byteOffset, plaintext.byteOffset + plaintext.byteLength) as ArrayBuffer
    );

    // Split ciphertext and tag (tag is last 16 bytes)
    const ciphertextBytes = new Uint8Array(ciphertextWithTag.slice(0, -16));
    const tag = new Uint8Array(ciphertextWithTag.slice(-16));

    const timestamp = Date.now();
    const sequenceNumber = ++this.sequenceNumber;

    // Create signature data
    const signatureData = this.createSignatureData(
      this.keyId,
      nonce,
      ciphertextBytes,
      tag,
      timestamp,
      sequenceNumber
    );

    // Sign - slice to get proper ArrayBuffer
    const signature = await crypto.subtle.sign(
      { name: 'ECDSA', hash: 'SHA-256' },
      this.signingPrivateKey,
      signatureData.buffer.slice(signatureData.byteOffset, signatureData.byteOffset + signatureData.byteLength) as ArrayBuffer
    );

    return {
      keyId: this.keyId,
      nonce: arrayBufferToBase64(nonce),
      ciphertext: arrayBufferToBase64(ciphertextBytes),
      tag: arrayBufferToBase64(tag),
      signature: arrayBufferToBase64(signature),
      timestamp,
      sequenceNumber,
    };
  }

  /**
   * Decrypt and verify a SecureEnvelope from the server.
   * checks both CURRENT and PREVIOUS keys.
   */
  async decryptAndVerify(envelope: SecureEnvelope): Promise<Uint8Array> {
    // Determine which key to use
    let usePrevious = false;
    let activeAesKey = this.aesKey;
    let activeServerSigningKey = this.serverSigningPublicKey;

    if (this.keyId && envelope.keyId === this.keyId) {
      usePrevious = false;
    } else if (this.previousKeyId && envelope.keyId === this.previousKeyId) {
      usePrevious = true;
      activeAesKey = this.previousAesKey;
      activeServerSigningKey = this.previousServerSigningPublicKey;
    } else {
        // Key ID mismatch. Checks if we can recover by reloading session (e.g. another tab/connection rotated the key)
        const canRecover = await this.restoreSession();
        if (canRecover) {
             // Re-evaluate keys after restore
             if (this.keyId && envelope.keyId === this.keyId) {
                 usePrevious = false;
                 activeAesKey = this.aesKey;
                 activeServerSigningKey = this.serverSigningPublicKey;
             } else if (this.previousKeyId && envelope.keyId === this.previousKeyId) {
                 usePrevious = true;
                 activeAesKey = this.previousAesKey;
                 activeServerSigningKey = this.previousServerSigningPublicKey;
             } else {
                 throw new Error(`Key ID mismatch after reload. Expected ${this.keyId}, got ${envelope.keyId}`);
             }
        } else {
            throw new Error(`Key ID mismatch. Expected ${this.keyId} (or ${this.previousKeyId}), got ${envelope.keyId}`);
        }
    }

    if (!activeAesKey || !activeServerSigningKey) {
        throw new Error('Matching key found but key material missing.');
    }

    const nonce = base64ToArrayBuffer(envelope.nonce);
    const ciphertext = base64ToArrayBuffer(envelope.ciphertext);
    const tag = base64ToArrayBuffer(envelope.tag);
    const signature = base64ToArrayBuffer(envelope.signature);

    // Verify signature
    const signatureData = this.createSignatureData(
      envelope.keyId,
      nonce,
      ciphertext,
      tag,
      envelope.timestamp,
      envelope.sequenceNumber
    );

    // Verify signature (use slice for safe ArrayBuffer extraction)
    const isValid = await crypto.subtle.verify(
      { name: 'ECDSA', hash: 'SHA-256' },
      activeServerSigningKey,
      signature.buffer.slice(signature.byteOffset, signature.byteOffset + signature.byteLength) as ArrayBuffer,
      signatureData.buffer.slice(signatureData.byteOffset, signatureData.byteOffset + signatureData.byteLength) as ArrayBuffer
    );

    if (!isValid) {
      throw new Error('Signature verification failed');
    }

    // 1. Validate timestamp for replay protection
    const now = Date.now();
    const age = (now - envelope.timestamp) / 1000;
    if (age > REPLAY_WINDOW_SECONDS || age < -CLOCK_SKEW_SECONDS) {
      throw new Error(`Message timestamp outside valid window: ${age.toFixed(1)}s`);
    }

    // 2. Validate sequence number per keyId
    const lastSeqForKey = this.lastReceivedSequencePerKey.get(envelope.keyId) ?? 0;
    
    if (!usePrevious) {
        // Strictly enforce for current key to prevent replays
        if (envelope.sequenceNumber <= lastSeqForKey) {
           throw new Error(`Sequence number regression for key ${envelope.keyId}: ${envelope.sequenceNumber} <= ${lastSeqForKey}`);
        }
        this.lastReceivedSequencePerKey.set(envelope.keyId, envelope.sequenceNumber);
    } else {
        // For previous key, just ensure it's > 0 to avoid obvious replays
        if (envelope.sequenceNumber <= 0) {
           throw new Error(`Invalid sequence number for previous key: ${envelope.sequenceNumber}`);
        }
    }

    // Recombine ciphertext and tag for decryption
    const ciphertextWithTag = new Uint8Array(ciphertext.length + tag.length);
    ciphertextWithTag.set(ciphertext, 0);
    ciphertextWithTag.set(tag, ciphertext.length);

    // Defensive copy for safety against buffer views
    const ivCopy = new Uint8Array(nonce).buffer;
    const dataCopy = new Uint8Array(ciphertextWithTag).buffer;

    // Decrypt
    const plaintext = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv: ivCopy, tagLength: GCM_TAG_LENGTH },
      activeAesKey,
      dataCopy
    );

    return new Uint8Array(plaintext);
  }

  /**
   * Save session state to sessionStorage.
   */
  async saveSession(): Promise<void> {
      try {
          if (!this.aesKey || !this.serverSigningPublicKey || !this.keyId || !this.signingPrivateKey) {
              return;
          }

          // Export keys to JWK
          const aesKeyJwk = await crypto.subtle.exportKey('jwk', this.aesKey);
          const serverSignJwk = await crypto.subtle.exportKey('jwk', this.serverSigningPublicKey);
          const signingKeyPrivateJwk = await crypto.subtle.exportKey('jwk', this.signingPrivateKey);
          
          // Export previous keys if available
          let prevAesJwk = null;
          let prevServerSignJwk = null;

          if (this.previousAesKey) {
              prevAesJwk = await crypto.subtle.exportKey('jwk', this.previousAesKey);
          }
          if (this.previousServerSigningPublicKey) {
              prevServerSignJwk = await crypto.subtle.exportKey('jwk', this.previousServerSigningPublicKey);
          }

          const minimalState = {
              keyId: this.keyId,
              aesKey: aesKeyJwk,
              serverSigningKey: serverSignJwk,
              clientSigningKey: signingKeyPrivateJwk,
              sequenceNumber: this.sequenceNumber,
              // Persist previous key state
              previousKeyId: this.previousKeyId,
              previousAesKey: prevAesJwk,
              previousServerSigningKey: prevServerSignJwk
          };

          sessionStorage.setItem(this.STORAGE_KEY, JSON.stringify(minimalState));
      } catch (e) {
          console.warn('Failed to save encryption session', e);
      }
  }

  /**
   * Restore session state from sessionStorage.
   */
  async restoreSession(): Promise<boolean> {
      try {
          const stored = sessionStorage.getItem(this.STORAGE_KEY);
          if (!stored) return false;

          const state = JSON.parse(stored);
          if (!state.keyId || !state.aesKey || !state.serverSigningKey || !state.clientSigningKey) {
              return false;
          }

          // Import Keys
          const aesKey = await crypto.subtle.importKey(
              'jwk', state.aesKey, { name: 'AES-GCM', length: AES_KEY_LENGTH }, true, ['encrypt', 'decrypt']
          );

          const serverSigningKey = await crypto.subtle.importKey(
              'jwk', state.serverSigningKey, { name: 'ECDSA', namedCurve: CURVE }, true, ['verify']
          );

          const clientSigningPrivateKey = await crypto.subtle.importKey(
              'jwk', state.clientSigningKey, { name: 'ECDSA', namedCurve: CURVE }, true, ['sign']
          );
          
          // Import previous keys if they exist
          if (state.previousKeyId && state.previousAesKey && state.previousServerSigningKey) {
             try {
                this.previousAesKey = await crypto.subtle.importKey(
                    'jwk', state.previousAesKey, { name: 'AES-GCM', length: AES_KEY_LENGTH }, true, ['encrypt', 'decrypt']
                );
                this.previousServerSigningPublicKey = await crypto.subtle.importKey(
                    'jwk', state.previousServerSigningKey, { name: 'ECDSA', namedCurve: CURVE }, true, ['verify']
                );
                this.previousKeyId = state.previousKeyId;
             } catch (e) {
                 console.warn("Failed to restore previous keys, continuing with current only", e);
             }
          }

          // Store only the private key - we don't need the public key for signing
          this.signingPrivateKey = clientSigningPrivateKey;

          this.aesKey = aesKey;
          this.serverSigningPublicKey = serverSigningKey;
          this.keyId = state.keyId;
          this.sequenceNumber = state.sequenceNumber || 0;
          
          // Add small buffer to sequence number to avoid server replay rejection on refresh
          this.sequenceNumber += MAX_SEQUENCE_GAP; 

          return true;
      } catch (e) {
          console.warn('Failed to restore encryption session', e);
          return false;
      }
  }

  /**
   * Create signature data for signing/verification.
   * Must match the server's signature format exactly.
   */
  private createSignatureData(
    keyId: string,
    nonce: Uint8Array,
    ciphertext: Uint8Array,
    tag: Uint8Array,
    timestamp: number,
    sequenceNumber: number
  ): Uint8Array {
    const encoder = new TextEncoder();
    const keyIdBytes = encoder.encode(keyId);
    
    // Calculate 7-bit encoded int length for keyId string length
    const getKeyIdLengthSize = (len: number) => {
      let size = 0;
      let v = len;
      while (v >= 0x80) {
        size++;
        v >>= 7;
      }
      size++;
      return size;
    };

    const keyIdLenSize = getKeyIdLengthSize(keyIdBytes.length);

    // Concatenate all parts fitting C# BinaryWriter format:
    // - String: 7-bit encoded length + UTF-8 bytes
    // - Byte Array: Raw bytes (NO length prefix in BinaryWriter.Write(byte[]))
    // - Long: 8 bytes little endian
    const totalLength = 
      keyIdLenSize + keyIdBytes.length + 
      nonce.length + 
      ciphertext.length + 
      tag.length + 
      8 + // timestamp
      8;  // sequenceNumber

    const result = new Uint8Array(totalLength);
    let offset = 0;

    // Write keyId length (7-bit encoded int)
    let v = keyIdBytes.length;
    while (v >= 0x80) {
      result[offset++] = (v | 0x80) & 0xFF;
      v >>= 7;
    }
    result[offset++] = v & 0xFF;

    // Write keyId bytes
    result.set(keyIdBytes, offset);
    offset += keyIdBytes.length;

    // Write nonce (raw bytes)
    result.set(nonce, offset);
    offset += nonce.length;

    // Write ciphertext (raw bytes)
    result.set(ciphertext, offset);
    offset += ciphertext.length;

    // Write tag (raw bytes)
    result.set(tag, offset);
    offset += tag.length;

    // Write timestamp (long, little endian)
    const view = new DataView(result.buffer);
    view.setBigInt64(offset, BigInt(timestamp), true);
    offset += 8;

    // Write sequenceNumber (long, little endian)
    view.setBigInt64(offset, BigInt(sequenceNumber), true);
    offset += 8;

    return result;
  }

  /**
   * Reset the encryptor state.
   */
  reset(): void {
    this.ecdhKeyPair = null;
    this.signingPrivateKey = null;
    this.aesKey = null;
    this.serverSigningPublicKey = null;
    this.keyId = null;
    this.sequenceNumber = 0;
    this.lastReceivedSequencePerKey.clear();
    this.previousKeyId = null;
    this.previousAesKey = null;
    this.previousServerSigningPublicKey = null;
    this.previousKeyExpiresAt = null;
    sessionStorage.removeItem(this.STORAGE_KEY);
  }

  /**
   * Clears expired previous key material.
   * Should be called periodically or before decryption.
   */
  clearExpiredPreviousKey(): void {
    if (this.previousKeyExpiresAt !== null && Date.now() > this.previousKeyExpiresAt) {
      this.previousKeyId = null;
      this.previousAesKey = null;
      this.previousServerSigningPublicKey = null;
      this.previousKeyExpiresAt = null;
    }
  }
}
