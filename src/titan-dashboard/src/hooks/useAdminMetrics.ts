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
 * Fetches a short-lived, single-use connection ticket for WebSocket auth.
 * This avoids exposing JWTs in server logs.
 */
async function fetchConnectionTicket(token: string): Promise<string | null> {
  try {
    const response = await fetch('/api/auth/connection-ticket', {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
    });
    if (!response.ok) {
      console.error('Failed to fetch connection ticket:', response.status);
      return null;
    }
    const data = await response.json();
    return data.ticket;
  } catch (err) {
    console.error('Error fetching connection ticket:', err);
    return null;
  }
}

/**
 * Custom hook for real-time rate limiting metrics via SignalR.
 * Replaces polling with push-based updates from the server.
 * Uses ticket-based auth to avoid JWT exposure in logs.
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

    let isCleanedUp = false;
    let retryCount = 0;
    const maxRetries = 10;

    // Start connection with ticket-based auth
    // Manual reconnection is required because tickets are single-use and passed in URL.
    // SignalR's withAutomaticReconnect doesn't refresh URL params on reconnect.
    const startConnection = async () => {
      // Check if we're still logged in before attempting connection
      const currentToken = localStorage.getItem('accessToken');
      if (!currentToken || isCleanedUp) {
        setConnectionState('disconnected');
        return;
      }

      setConnectionState('connecting');
      setError(null);
      
      try {
        // Dispose any existing connection before creating a new one
        if (connectionRef.current) {
          try {
            await connectionRef.current.stop();
          } catch {
            // Ignore stop errors
          }
          connectionRef.current = null;
        }

        // Fetch a fresh connection ticket for each connection attempt
        const ticket = await fetchConnectionTicket(currentToken);
        if (!ticket) {
          throw new Error('Failed to obtain connection ticket');
        }

        if (isCleanedUp) return; // Guard against cleanup during async operation

        // Build connection with ticket in URL (not JWT)
        // No withAutomaticReconnect - we handle reconnection manually with fresh tickets
        const connection = new signalR.HubConnectionBuilder()
          .withUrl(`/hubs/admin-metrics?ticket=${encodeURIComponent(ticket)}`)
          .configureLogging(signalR.LogLevel.Warning)
          .build();

        connectionRef.current = connection;

        // Handle metrics updates
        connection.on('MetricsUpdated', (data: RateLimitMetrics) => {
          setMetrics(data);
        });

        // Manual reconnection on close - creates new connection with fresh ticket
        connection.onclose((err) => {
          setConnectionState('disconnected');
          
          if (isCleanedUp) return;
          
          if (err) {
            setError(`Connection closed: ${err.message}`);
            
            // Attempt manual reconnection with exponential backoff
            if (retryCount < maxRetries) {
              const delay = Math.min(2000 * Math.pow(2, retryCount), 30000);
              retryCount++;
              console.log(`SignalR reconnecting in ${delay}ms (attempt ${retryCount}/${maxRetries})`);
              setTimeout(() => startConnection(), delay);
            } else {
              setError('Connection failed after maximum retries. Please refresh the page.');
            }
          }
        });

        await connection.start();
        setConnectionState('connected');
        retryCount = 0; // Reset retry counter on successful connection
        
        // Subscribe to metrics updates
        await connection.invoke('SubscribeToMetrics');
      } catch (err) {
        console.error('SignalR connection failed:', err);
        setConnectionState('error');
        setError(err instanceof Error ? err.message : 'Connection failed');
        
        // Retry on initial connection failure too
        if (!isCleanedUp && retryCount < maxRetries) {
          const delay = Math.min(2000 * Math.pow(2, retryCount), 30000);
          retryCount++;
          console.log(`SignalR retrying in ${delay}ms (attempt ${retryCount}/${maxRetries})`);
          setTimeout(() => startConnection(), delay);
        }
      }
    };

    startConnection();

    // Cleanup
    return () => {
      isCleanedUp = true;
      const connection = connectionRef.current;
      if (connection && connection.state !== signalR.HubConnectionState.Disconnected) {
        connection.invoke('UnsubscribeFromMetrics').catch(() => {});
        connection.stop().catch(console.error);
      }
    };
  }, [getToken]);

  return { metrics, connectionState, refresh, clearTimeout, clearBucket, error };
}
