/**
 * SignalR encryption wrapper.
 * Wraps a SignalR HubConnection to automatically encrypt/decrypt messages.
 */

import * as signalR from '@microsoft/signalr';
import { Encryptor } from './encryptor';
import type { SecureEnvelope, EncryptionConfig, KeyExchangeResponse } from './types';

/**
 * Options for creating an encrypted SignalR connection.
 */
export interface EncryptedConnectionOptions {
  /** Base URL for hubs (e.g., '' for relative, or 'https://api.example.com') */
  baseUrl?: string;
  /** Function to get authentication token */
  getToken: () => string | null;
  /** Whether to automatically perform key exchange when encryption is enabled */
  autoKeyExchange?: boolean;
}

/**
 * Utility to convert buffer to Base64 string.
 */
function arrayBufferToBase64(buffer: ArrayBuffer | Uint8Array): string {
  const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

/**
 * Encrypted SignalR connection manager.
 * Handles key exchange and provides encrypted hub methods.
 */
export class EncryptedSignalRConnection {
  private encryptor: Encryptor;
  private encryptionHub: signalR.HubConnection | null = null;
  private targetHub: signalR.HubConnection | null = null;
  private isEncryptionEnabled = false;
  private isEncryptionRequired = false;
  private options: EncryptedConnectionOptions;

  constructor(options: EncryptedConnectionOptions) {
    this.options = options;
    this.encryptor = new Encryptor();
  }

  /**
   * Whether encryption is currently active.
   */
  get encryptionActive(): boolean {
    return this.isEncryptionEnabled && this.encryptor.isInitialized;
  }

  /**
   * Get the current encryption config from server.
   */
  async getEncryptionConfig(): Promise<EncryptionConfig> {
    const token = this.options.getToken();
    if (!token) {
      throw new Error('No authentication token available');
    }

    // Create temporary connection to get config
    const tempHub = new signalR.HubConnectionBuilder()
      .withUrl(`${this.options.baseUrl || ''}/encryptionHub`, {
        accessTokenFactory: () => token,
      })
      .build();

    try {
      await tempHub.start();
      const config = await tempHub.invoke<EncryptionConfig>('GetConfig');
      await tempHub.stop();
      return config;
    } catch (error) {
      await tempHub.stop().catch(() => {});
      throw error;
    }
  }

  /**
   * Initialize encryption by connecting to EncryptionHub and performing key exchange.
   */
  async initializeEncryption(): Promise<boolean> {
    const token = this.options.getToken();
    if (!token) {
      console.warn('[EncryptedSignalR] No token available, skipping encryption');
      return false;
    }

    try {
      // Check encryption config
      const config = await this.getEncryptionConfig();
      this.isEncryptionEnabled = config.enabled;
      this.isEncryptionRequired = config.requireEncryption;

      if (!config.enabled) {
        console.log('[EncryptedSignalR] Encryption is disabled on server');
        return false;
      }

      // Try to restore previous session state to handle graceful rotation on refresh
      // This allows us to decrypt messages sent with the "old" key while we negotiate a new one
      const restored = await this.encryptor.restoreSession();
      if (restored) {
          console.log('[EncryptedSignalR] Restored previous encryption session');
      }

      // Connect to encryption hub
      this.encryptionHub = new signalR.HubConnectionBuilder()
        .withUrl(`${this.options.baseUrl || ''}/encryptionHub`, {
          accessTokenFactory: () => token,
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();

      await this.encryptionHub.start();
      console.log('[EncryptedSignalR] Connected to encryption hub');

      // Perform key exchange only if not restored
      if (!restored) {
          const keyExchangeRequest = await this.encryptor.createKeyExchangeRequest();
          const response = await this.encryptionHub.invoke<KeyExchangeResponse>(
            'KeyExchange',
            keyExchangeRequest
          );
          await this.encryptor.completeKeyExchange(response);
          console.log('[EncryptedSignalR] Key exchange completed');
      } else {
          console.log('[EncryptedSignalR] Skipping key exchange (session restored)');
      }

      return true;
    } catch (error) {
      console.error('[EncryptedSignalR] Failed to initialize encryption:', error);
      this.encryptor.reset(); // Clear bad state
      if (this.isEncryptionRequired) {
        throw error;
      }
      return false;
    }
  }

  /**
   * Create a hub connection with encryption support.
   * If encryption is enabled, messages will be automatically encrypted/decrypted.
   */
  async connectToHub(hubPath: string): Promise<signalR.HubConnection> {
    const token = this.options.getToken();
    if (!token) {
      throw new Error('No authentication token available');
    }

    // Initialize encryption if needed
    if (this.options.autoKeyExchange !== false && !this.encryptor.isInitialized) {
      await this.initializeEncryption();
    }

    // Create hub connection
    this.targetHub = new signalR.HubConnectionBuilder()
      .withUrl(`${this.options.baseUrl || ''}${hubPath}`, {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
        },
      })
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    return this.targetHub;
  }

  /**
   * Invoke a hub method with automatic encryption.
   * @param hubConnection The hub connection to use
   * @param methodName The method to invoke
   * @param args Arguments to pass (will be encrypted if encryption is active)
   */
  async invoke<T>(
    hubConnection: signalR.HubConnection,
    methodName: string,
    ...args: unknown[]
  ): Promise<T> {
    if (this.encryptionActive) {
      try {
          // Create EncryptedInvocation object
          // For single arg, we send it directly as JSON. For multi, we send as array.
          const payloadObj = args.length === 1 ? args[0] : args;
          const payloadJson = JSON.stringify(payloadObj);
          const payloadBytes = new TextEncoder().encode(payloadJson);
          
          // Convert payload to Base64 string because C# System.Text.Json expects Base64 for byte[] properties
          const payloadBase64 = arrayBufferToBase64(payloadBytes);

          const invocation = {
            target: methodName,
            payload: payloadBase64
          };

          const invocationJson = JSON.stringify(invocation);
          const plaintext = new TextEncoder().encode(invocationJson);
          const envelope = await this.encryptor.encryptAndSign(plaintext);
          
          // Invoke the generic encrypted gateway
          const result = await hubConnection.invoke<SecureEnvelope>('__encrypted__', envelope);
          
          // Check if result is encrypted
          if (result && typeof result === 'object' && 'keyId' in result) {
            // Decrypt response
            const decrypted = await this.encryptor.decryptAndVerify(result);
            const decoded = new TextDecoder().decode(decrypted);
            return JSON.parse(decoded) as T;
          }
          
          return result as unknown as T;
      } catch (error) {
          console.error('[EncryptedSignalR] Invoke failed with encryption:', error);
          // If it's a security error, server might have rejected our key. Reset.
          this.encryptor.reset(); 
          throw error;
      }
    } else {
      // Direct invocation without encryption
      return hubConnection.invoke<T>(methodName, ...args);
    }
  }

  /**
   * Register a handler for encrypted incoming messages.
   * @param hubConnection The hub connection to use
   * @param methodName The method name to listen for
   * @param handler The handler function (receives decrypted data)
   */
  on<T>(
    hubConnection: signalR.HubConnection,
    methodName: string,
    handler: (data: T) => void
  ): void {
    if (this.encryptionActive) {
      // Register handler that decrypts incoming messages
      hubConnection.on(methodName, async (envelope: SecureEnvelope | T) => {
        try {
          // Check if it's an encrypted envelope
          if (envelope && typeof envelope === 'object' && 'keyId' in envelope) {
            const decrypted = await this.encryptor.decryptAndVerify(envelope as SecureEnvelope);
            const decoded = new TextDecoder().decode(decrypted);
            handler(JSON.parse(decoded) as T);
          } else {
            // Not encrypted, pass through
            handler(envelope as T);
          }
        } catch (error) {
          console.error('[EncryptedSignalR] Failed to decrypt message:', error);
          this.encryptor.reset(); // Reset session on decryption failure so next reload recovers
        }
      });
    } else {
      // Direct registration without decryption
      hubConnection.on(methodName, handler);
    }
  }

  /**
   * Disconnect all connections.
   */
  async disconnect(): Promise<void> {
    if (this.targetHub?.state !== signalR.HubConnectionState.Disconnected) {
      await this.targetHub?.stop();
    }
    if (this.encryptionHub?.state !== signalR.HubConnectionState.Disconnected) {
      await this.encryptionHub?.stop();
    }
    this.encryptor.reset();
  }
}

/**
 * Create a simple encrypted connection helper.
 */
export function createEncryptedConnection(options: EncryptedConnectionOptions): EncryptedSignalRConnection {
  return new EncryptedSignalRConnection(options);
}
