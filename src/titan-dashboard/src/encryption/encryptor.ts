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



/**
 * Encryption client for SignalR hub communication.
 * Handles key exchange, encryption, decryption, and signing.
 * Supports key rotation grace periods and session persistence.
 */
export class Encryptor {
  private ecdhKeyPair: CryptoKeyPair | null = null;
  // We only need the private key for signing - publicKey is null after restoration from session
  private signingPrivateKey: CryptoKey | null = null;
  // Store the signing public key in base64 format for rotation acks
  private signingPublicKeyBase64: string | null = null;
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
  
  /** Grace period duration in milliseconds (set from server during key exchange) */
  private gracePeriodMs = 30 * 1000; // Default to 30s as fallback

  // Use localStorage for cross-tab sharing of encryption state
  private readonly STORAGE_KEY = 'titan_encryption_session';
  // How old the saved session can be before we consider it stale and do fresh key exchange
  private readonly SESSION_FRESHNESS_MS = 5 * 60 * 1000; // 5 minutes

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
    
    // Store the signing public key for rotation acks
    this.signingPublicKeyBase64 = arrayBufferToBase64(signingPublicKey);

    return {
      clientPublicKey: arrayBufferToBase64(ecdhPublicKey),
      clientSigningPublicKey: this.signingPublicKeyBase64,
    };
  }

  /**
   * Complete key exchange using server's response.
   */
  async completeKeyExchange(response: KeyExchangeResponse): Promise<void> {
    if (!this.ecdhKeyPair) {
      throw new Error('Key exchange not initiated. Call createKeyExchangeRequest first.');
    }

    // Import server's ECDH public key - use slice for safe ArrayBuffer extraction
    const serverPublicKeyBytes = base64ToArrayBuffer(response.serverPublicKey);
    const serverEcdhPublicKey = await crypto.subtle.importKey(
      'spki',
      serverPublicKeyBytes.buffer.slice(serverPublicKeyBytes.byteOffset, serverPublicKeyBytes.byteOffset + serverPublicKeyBytes.byteLength) as ArrayBuffer,
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

    // Import server's new signing public key - use slice for safe ArrayBuffer extraction
    const serverSigningKeyBytes = base64ToArrayBuffer(response.serverSigningPublicKey);
    const newServerSigningKey = await crypto.subtle.importKey(
      'spki',
      serverSigningKeyBytes.buffer.slice(serverSigningKeyBytes.byteOffset, serverSigningKeyBytes.byteOffset + serverSigningKeyBytes.byteLength) as ArrayBuffer,
      { name: 'ECDSA', namedCurve: CURVE },
      true,
      ['verify']
    );

    // Rotation: Move current keys to previous with expiry tracking
    if (this.keyId && this.aesKey && this.serverSigningPublicKey) {
      this.previousKeyId = this.keyId;
      this.previousAesKey = this.aesKey;
      this.previousServerSigningPublicKey = this.serverSigningPublicKey;
      this.previousKeyExpiresAt = Date.now() + this.gracePeriodMs;
    }

    // Set new keys
    this.aesKey = newAesKey;
    this.serverSigningPublicKey = newServerSigningKey;
    this.keyId = response.keyId;
    this.sequenceNumber = 0; // Reset sequence for new key
    
    // Store server's grace period
    this.gracePeriodMs = (response.gracePeriodSeconds || 30) * 1000;
    
    // Clear ephemeral ECDH keypair - no longer needed after key derivation
    this.ecdhKeyPair = null;

    // Persist immediately
    await this.saveSession();
  }

  /**
   * Handle a key rotation request from the server.
   * Generates new ECDH keypair and derives new AES key.
   * Returns the client's new public key for the server.
   */
  async handleKeyRotation(request: KeyRotationRequest): Promise<{ clientPublicKey: string; clientSigningPublicKey: string }> {
    if (!this.aesKey || !this.keyId || !this.signingPublicKeyBase64) {
      throw new Error('Cannot rotate keys: encryption not initialized or signing key missing');
    }

    // Move current keys to previous for grace period
    this.previousKeyId = this.keyId;
    this.previousAesKey = this.aesKey;
    this.previousServerSigningPublicKey = this.serverSigningPublicKey;
    this.previousKeyExpiresAt = Date.now() + this.gracePeriodMs;

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
    
    // Clear ephemeral ECDH keypair - no longer needed after key derivation
    this.ecdhKeyPair = null;

    return {
      clientPublicKey: arrayBufferToBase64(clientPublicKeySpki),
      clientSigningPublicKey: this.signingPublicKeyBase64!,
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
    // Clear any expired previous key material before attempting decryption
    this.clearExpiredPreviousKey();

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
        const { restored: canRecover } = await this.restoreSession();
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
                 // Log detailed mismatch for debugging, throw generic error
                 console.error('[Encryptor] Key ID mismatch after potential reload. Current:', this.keyId, 'Previous:', this.previousKeyId, 'Received:', envelope.keyId);
                 throw new Error('Key ID mismatch');
             }
        } else {
            // Log detailed mismatch for debugging, throw generic error
            console.error('[Encryptor] Key ID mismatch. Current:', this.keyId, 'Previous:', this.previousKeyId, 'Received:', envelope.keyId);
            throw new Error('Key ID mismatch');
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
        // For previous key, also enforce sequence ordering to prevent replays during grace period
        if (envelope.sequenceNumber <= lastSeqForKey) {
           throw new Error(`Sequence number regression for previous key ${envelope.keyId}: ${envelope.sequenceNumber} <= ${lastSeqForKey}`);
        }
        this.lastReceivedSequencePerKey.set(envelope.keyId, envelope.sequenceNumber);
    }

    // Recombine ciphertext and tag for decryption
    const ciphertextWithTag = new Uint8Array(ciphertext.length + tag.length);
    ciphertextWithTag.set(ciphertext, 0);
    ciphertextWithTag.set(tag, ciphertext.length);

    // Defensive copy for safety against buffer views (use consistent slice pattern)
    const ivCopy = nonce.buffer.slice(nonce.byteOffset, nonce.byteOffset + nonce.byteLength) as ArrayBuffer;
    const dataCopy = ciphertextWithTag.buffer.slice(ciphertextWithTag.byteOffset, ciphertextWithTag.byteOffset + ciphertextWithTag.byteLength) as ArrayBuffer;

    // Decrypt
    const plaintext = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv: ivCopy, tagLength: GCM_TAG_LENGTH },
      activeAesKey,
      dataCopy
    );

    return new Uint8Array(plaintext);
  }

  /**
   * Save session state to localStorage (cross-tab sharing).
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
              clientSigningPublicKeyBase64: this.signingPublicKeyBase64,
              sequenceNumber: this.sequenceNumber,
              // Persist previous key state
              previousKeyId: this.previousKeyId,
              previousAesKey: prevAesJwk,
              previousServerSigningKey: prevServerSignJwk,
              previousKeyExpiresAt: this.previousKeyExpiresAt,
              // Timestamp for freshness checking (multi-tab support)
              savedAt: Date.now()
          };

          localStorage.setItem(this.STORAGE_KEY, JSON.stringify(minimalState));
      } catch (e) {
          console.warn('Failed to save encryption session', e);
      }
  }

  /**
   * Restore session state from localStorage (cross-tab sharing).
   * Returns { restored: boolean, isFresh: boolean } where isFresh indicates
   * the session was saved recently (within SESSION_FRESHNESS_MS) by another tab.
   */
  async restoreSession(): Promise<{ restored: boolean; isFresh: boolean }> {
      try {
          const stored = localStorage.getItem(this.STORAGE_KEY);
          if (!stored) return { restored: false, isFresh: false };

          const state = JSON.parse(stored);
          if (!state.keyId || !state.aesKey || !state.serverSigningKey || !state.clientSigningKey) {
              return { restored: false, isFresh: false };
          }

          // Check if the session is fresh (saved recently by another tab)
          const savedAt = state.savedAt || 0;
          const isFresh = (Date.now() - savedAt) < this.SESSION_FRESHNESS_MS;

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
                // Restore previous key expiration timestamp
                this.previousKeyExpiresAt = state.previousKeyExpiresAt ?? null;
             } catch (e) {
                 console.warn("Failed to restore previous keys, continuing with current only", e);
             }
          }

          // Store only the private key - we don't need the public key for signing
          this.signingPrivateKey = clientSigningPrivateKey;
          // Restore the signing public key for rotation acks
          this.signingPublicKeyBase64 = state.clientSigningPublicKeyBase64 || null;

          this.aesKey = aesKey;
          this.serverSigningPublicKey = serverSigningKey;
          this.keyId = state.keyId;
          // Restore sequence number for fresh sessions (multi-tab sharing)
          // Add buffer to avoid collision with other tab's concurrent messages
          this.sequenceNumber = (state.sequenceNumber || 0) + 10;

          return { restored: true, isFresh };
      } catch (e) {
          console.warn('Failed to restore encryption session', e);
          return { restored: false, isFresh: false };
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
    const view = new DataView(result.buffer, result.byteOffset, result.byteLength);
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
    this.signingPublicKeyBase64 = null;
    this.aesKey = null;
    this.serverSigningPublicKey = null;
    this.keyId = null;
    this.sequenceNumber = 0;
    this.lastReceivedSequencePerKey.clear();
    this.previousKeyId = null;
    this.previousAesKey = null;
    this.previousServerSigningPublicKey = null;
    this.previousKeyExpiresAt = null;
    localStorage.removeItem(this.STORAGE_KEY);
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
