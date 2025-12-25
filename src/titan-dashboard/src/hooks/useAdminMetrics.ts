import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { createEncryptedConnection, type EncryptedSignalRConnection } from '../encryption';

export interface RateLimitBucket {
  partitionKey: string;
  policyName: string;
  periodSeconds: number;
  currentCount: number;
  secondsRemaining: number;
}

export interface RateLimitTimeout {
  partitionKey: string;
  policyName: string;
  secondsRemaining: number;
}

export interface RateLimitMetrics {
  activeBuckets: number;
  activeTimeouts: number;
  buckets: RateLimitBucket[];
  timeouts: RateLimitTimeout[];
}

type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'error';

interface UseAdminMetricsReturn {
  metrics: RateLimitMetrics | null;
  connectionState: ConnectionState;
  refresh: () => void;
  clearTimeout: (partitionKey: string, policyName: string) => Promise<boolean>;
  clearBucket: (partitionKey: string) => Promise<number>;
  error: string | null;
  encryptionActive: boolean;
}

/**
 * Custom hook for real-time rate limiting metrics via SignalR.
 * Uses EncryptedSignalRConnection for secure application-layer encryption.
 */
export function useAdminMetrics(): UseAdminMetricsReturn {
  const connectionManagerRef = useRef<EncryptedSignalRConnection | null>(null);
  const hubConnectionRef = useRef<signalR.HubConnection | null>(null);
  const [metrics, setMetrics] = useState<RateLimitMetrics | null>(null);
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [error, setError] = useState<string | null>(null);
  const [encryptionActive, setEncryptionActive] = useState(false);
  
  const apiBaseUrl = import.meta.env.VITE_API_URL || '';
  const getToken = useCallback(() => localStorage.getItem('sessionId'), []);

  const refresh = useCallback(async () => {
    if (connectionManagerRef.current && hubConnectionRef.current?.state === signalR.HubConnectionState.Connected) {
      try {
        await connectionManagerRef.current.invoke(hubConnectionRef.current, 'RefreshMetrics');
      } catch (err) {
        console.error('Failed to refresh metrics:', err);
      }
    }
  }, []);

  const clearTimeout = useCallback(async (partitionKey: string, policyName: string): Promise<boolean> => {
    if (connectionManagerRef.current && hubConnectionRef.current?.state === signalR.HubConnectionState.Connected) {
      try {
        return await connectionManagerRef.current.invoke<boolean>(
          hubConnectionRef.current, 
          'ClearTimeout', 
          partitionKey, 
          policyName
        );
      } catch (err) {
        console.error('Failed to clear timeout:', err);
      }
    }
    return false;
  }, []);

  const clearBucket = useCallback(async (partitionKey: string): Promise<number> => {
    if (connectionManagerRef.current && hubConnectionRef.current?.state === signalR.HubConnectionState.Connected) {
      try {
        return await connectionManagerRef.current.invoke<number>(
          hubConnectionRef.current, 
          'ClearBucket', 
          partitionKey
        );
      } catch (err) {
        console.error('Failed to clear bucket:', err);
      }
    }
    return 0;
  }, []);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      setConnectionState('disconnected');
      return;
    }

    let isEffectActive = true;
    
    const initialize = async () => {
      setConnectionState('connecting');
      setError(null);

      try {
        const manager = createEncryptedConnection({
          baseUrl: apiBaseUrl,
          getToken,
          autoKeyExchange: true
        });
        connectionManagerRef.current = manager;

        // Initialize encryption (GetConfig -> KeyExchange)
        const encryptionSuccess = await manager.initializeEncryption();
        if (!isEffectActive) return;
        
        setEncryptionActive(encryptionSuccess);
        
        // Connect to the admin metrics hub
        const hub = await manager.connectToHub('/hub/admin-metrics');
        hubConnectionRef.current = hub;
        if (!isEffectActive) return;

        // Register event handlers via manager (handles decryption)
        manager.on<RateLimitMetrics>(hub, 'MetricsUpdated', (data) => {
          if (isEffectActive) setMetrics(data);
        });

        // Connection lifecycle
        hub.onreconnecting(() => setConnectionState('connecting'));
        hub.onreconnected(() => {
          setConnectionState('connected');
          manager.invoke(hub, 'SubscribeToMetrics').catch(console.error);
        });
        hub.onclose((err) => {
          setConnectionState('disconnected');
          if (err) setError(`Connection lost: ${err.message}`);
        });

        await hub.start();
        if (!isEffectActive) return;

        setConnectionState('connected');
        await manager.invoke(hub, 'SubscribeToMetrics');
        console.log('[useAdminMetrics] Connected and subscribed');

      } catch (err) {
        if (!isEffectActive) return;
        
        console.error('[useAdminMetrics] Init failed:', err);
        setConnectionState('error');
        setError(err instanceof Error ? `Connection failed: ${err.message}` : 'Connection failed');
      }
    };

    initialize();

    return () => {
      isEffectActive = false;
      if (hubConnectionRef.current) {
        hubConnectionRef.current.stop().catch(() => {});
      }
      if (connectionManagerRef.current) {
        connectionManagerRef.current.disconnect().catch(() => {});
      }
    };
  }, [getToken, apiBaseUrl]);

  return { metrics, connectionState, refresh, clearTimeout, clearBucket, error, encryptionActive };
}
