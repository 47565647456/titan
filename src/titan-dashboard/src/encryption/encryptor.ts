/**
 * Client-side encryption module for SignalR hub communication.
 * Uses Web Crypto API for ECDH key exchange, ECDSA signing, and AES-256-GCM encryption.
 * 
 * Mirrors the C# ClientEncryptor from Titan.Client.Encryption.
 */

import type { SecureEnvelope, KeyExchangeRequest, KeyExchangeResponse } from './types';

// Named curve for ECDH and ECDSA
const CURVE = 'P-256';
const AES_KEY_LENGTH = 256;
const NONCE_LENGTH = 12;
const GCM_TAG_LENGTH = 128; // Bits
const REPLAY_WINDOW_SECONDS = 60;
const CLOCK_SKEW_SECONDS = 5;

/**
 * Utility functions for Base64 encoding/decoding
 */
function arrayBufferToBase64(buffer: ArrayBuffer | Uint8Array): string {
  const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

function base64ToArrayBuffer(base64: string): Uint8Array {
  const binary = atob(base64);
  const buffer = new ArrayBuffer(binary.length);
  const bytes = new Uint8Array(buffer);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

/**
 * Encryption client for SignalR hub communication.
 * Handles key exchange, encryption, decryption, and signing.
 */
export class Encryptor {
  private ecdhKeyPair: CryptoKeyPair | null = null;
  private signingKeyPair: CryptoKeyPair | null = null;
  private aesKey: CryptoKey | null = null;
  private serverSigningPublicKey: CryptoKey | null = null;
  private keyId: string | null = null;
  private sequenceNumber = 0;
  private lastReceivedSequenceNumber = 0;

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
    this.signingKeyPair = await crypto.subtle.generateKey(
      { name: 'ECDSA', namedCurve: CURVE },
      true,
      ['sign', 'verify']
    );

    // Export public keys in SPKI format
    const ecdhPublicKey = await crypto.subtle.exportKey('spki', this.ecdhKeyPair.publicKey);
    const signingPublicKey = await crypto.subtle.exportKey('spki', this.signingKeyPair.publicKey);

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
    
    // Derive AES key from shared secret using HKDF
    const sharedSecretKey = await crypto.subtle.importKey(
      'raw',
      sharedSecret,
      { name: 'HKDF' },
      false,
      ['deriveKey']
    );

    this.aesKey = await crypto.subtle.deriveKey(
      {
        name: 'HKDF',
        hash: 'SHA-256',
        salt: new Uint8Array(32), // HKDF spec: null salt defaults to HashLen zeros (32 for SHA-256)
        info: new TextEncoder().encode('titan-encryption-key'),
      },
      sharedSecretKey,
      { name: 'AES-GCM', length: AES_KEY_LENGTH },
      false,
      ['encrypt', 'decrypt']
    );

    // Import server's signing public key
    const serverSigningKeyBytes = base64ToArrayBuffer(response.serverSigningPublicKey);
    this.serverSigningPublicKey = await crypto.subtle.importKey(
      'spki',
      serverSigningKeyBytes.buffer as ArrayBuffer,
      { name: 'ECDSA', namedCurve: CURVE },
      false,
      ['verify']
    );

    this.keyId = response.keyId;
    this.sequenceNumber = 0;
  }

  /**
   * Encrypt and sign data, returning a SecureEnvelope.
   */
  async encryptAndSign(plaintext: Uint8Array): Promise<SecureEnvelope> {
    if (!this.aesKey || !this.signingKeyPair || !this.keyId) {
      throw new Error('Encryption not initialized. Complete key exchange first.');
    }

    // Generate nonce (12 bytes)
    const nonce = new Uint8Array(NONCE_LENGTH);
    crypto.getRandomValues(nonce);

    // Encrypt with AES-GCM
    const ciphertextWithTag = await crypto.subtle.encrypt(
      { name: 'AES-GCM', iv: nonce, tagLength: GCM_TAG_LENGTH },
      this.aesKey,
      plaintext.buffer as ArrayBuffer
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

    // Sign
    const signature = await crypto.subtle.sign(
      { name: 'ECDSA', hash: 'SHA-256' },
      this.signingKeyPair.privateKey,
      signatureData.buffer as ArrayBuffer
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
   */
  async decryptAndVerify(envelope: SecureEnvelope): Promise<Uint8Array> {
    if (!this.aesKey || !this.serverSigningPublicKey || !this.keyId) {
      throw new Error('Encryption not initialized. Complete key exchange first.');
    }

    if (envelope.keyId !== this.keyId) {
      throw new Error(`Key ID mismatch. Expected ${this.keyId}, got ${envelope.keyId}`);
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

    const isValid = await crypto.subtle.verify(
      { name: 'ECDSA', hash: 'SHA-256' },
      this.serverSigningPublicKey,
      signature.buffer as ArrayBuffer,
      signatureData.buffer as ArrayBuffer
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

    // 2. Validate sequence number (monotonically increasing)
    if (envelope.sequenceNumber <= this.lastReceivedSequenceNumber) {
      throw new Error(`Sequence number regression detected: ${envelope.sequenceNumber} <= ${this.lastReceivedSequenceNumber}`);
    }
    this.lastReceivedSequenceNumber = envelope.sequenceNumber;

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
      this.aesKey,
      dataCopy
    );

    return new Uint8Array(plaintext);
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
    this.signingKeyPair = null;
    this.aesKey = null;
    this.serverSigningPublicKey = null;
    this.keyId = null;
    this.sequenceNumber = 0;
    this.lastReceivedSequenceNumber = 0;
  }
}
