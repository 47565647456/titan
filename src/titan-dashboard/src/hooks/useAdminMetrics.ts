import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';

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
}

/**
 * Custom hook for real-time rate limiting metrics via SignalR.
 * Replaces polling with push-based updates from the server.
 */
export function useAdminMetrics(): UseAdminMetricsReturn {
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const [metrics, setMetrics] = useState<RateLimitMetrics | null>(null);
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [error, setError] = useState<string | null>(null);
  
  // Get token from localStorage (same as API client)
  const getToken = useCallback(() => localStorage.getItem('accessToken'), []);

  // Manual refresh request
  const refresh = useCallback(() => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      connectionRef.current.invoke('RefreshMetrics').catch(err => {
        console.error('Failed to refresh metrics:', err);
      });
    }
  }, []);

  // Clear a specific timeout
  const clearTimeout = useCallback(async (partitionKey: string, policyName: string): Promise<boolean> => {
    if (connectionRef.current?.state !== signalR.HubConnectionState.Connected) {
      return false;
    }
    try {
      const result = await connectionRef.current.invoke<boolean>('ClearTimeout', partitionKey, policyName);
      return result;
    } catch (err) {
      console.error('Failed to clear timeout:', err);
      return false;
    }
  }, []);

  // Clear all buckets for a partition key
  const clearBucket = useCallback(async (partitionKey: string): Promise<number> => {
    if (connectionRef.current?.state !== signalR.HubConnectionState.Connected) {
      return 0;
    }
    try {
      const count = await connectionRef.current.invoke<number>('ClearBucket', partitionKey);
      return count;
    } catch (err) {
      console.error('Failed to clear bucket:', err);
      return 0;
    }
  }, []);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      setConnectionState('disconnected');
      return;
    }

    // Build connection
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/admin-metrics', {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          // Exponential backoff: 1s, 2s, 4s, 8s, max 30s
          return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
        },
      })
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    // Handle metrics updates
    connection.on('MetricsUpdated', (data: RateLimitMetrics) => {
      setMetrics(data);
    });

    // Connection state handlers
    connection.onreconnecting(() => {
      setConnectionState('connecting');
    });

    connection.onreconnected(() => {
      setConnectionState('connected');
      // Re-subscribe after reconnect
      connection.invoke('SubscribeToMetrics').catch(console.error);
    });

    connection.onclose((err) => {
      setConnectionState('disconnected');
      if (err) {
        setError(`Connection closed: ${err.message}`);
      }
    });

    // Start connection
    const startConnection = async () => {
      setConnectionState('connecting');
      setError(null);
      
      try {
        await connection.start();
        setConnectionState('connected');
        
        // Subscribe to metrics updates
        await connection.invoke('SubscribeToMetrics');
      } catch (err) {
        console.error('SignalR connection failed:', err);
        setConnectionState('error');
        setError(err instanceof Error ? err.message : 'Connection failed');
      }
    };

    startConnection();

    // Cleanup
    return () => {
      if (connection.state !== signalR.HubConnectionState.Disconnected) {
        connection.invoke('UnsubscribeFromMetrics').catch(() => {});
        connection.stop().catch(console.error);
      }
    };
  }, [getToken]);

  return { metrics, connectionState, refresh, clearTimeout, clearBucket, error };
}
