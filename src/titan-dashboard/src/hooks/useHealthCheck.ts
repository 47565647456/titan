import { useQuery } from '@tanstack/react-query';
import { healthApi, type HealthCheckResponse } from '../api/client';

/** Display name mapping for health check entries */
const CHECK_DISPLAY_NAMES: Record<string, string> = {
  'self': 'API Server',
  'titan-db': 'Database',
  'orleans-redis': 'Redis',
};

export interface HealthCheckItem {
  name: string;
  displayName: string;
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  duration: string;
}

export interface UseHealthCheckReturn {
  overallStatus: 'Healthy' | 'Degraded' | 'Unhealthy' | 'Unknown';
  checks: HealthCheckItem[];
  isLoading: boolean;
  isError: boolean;
  refetch: () => void;
}

/**
 * Hook for fetching and polling system health status.
 * Polls every 30 seconds to keep dashboard status current.
 * Includes resilience features: timeouts, retries with backoff, and state preservation.
 */
export function useHealthCheck(): UseHealthCheckReturn {
  const { data, isLoading, isError, refetch } = useQuery<HealthCheckResponse>({
    queryKey: ['health'],
    queryFn: ({ signal }) => healthApi.getStatus(signal),
    refetchInterval: 30000, // Poll every 30 seconds
    staleTime: 10000, // Consider data fresh for 10 seconds
    // Retry with exponential backoff (1s, 2s, 4s)
    retry: 3,
    retryDelay: (attemptIndex) => Math.min(1000 * 2 ** attemptIndex, 30000),
    // Keep previous data during refetch errors
    placeholderData: (previousData) => previousData,
    // Don't refetch on window focus (we already poll)
    refetchOnWindowFocus: false,
    // Continue polling in background tabs
    refetchIntervalInBackground: true,
  });

  const checks: HealthCheckItem[] = data?.checks.map((check) => ({
    name: check.name,
    displayName: CHECK_DISPLAY_NAMES[check.name] || check.name,
    status: check.status,
    duration: check.duration,
  })) ?? [];

  return {
    overallStatus: isError ? 'Unknown' : (data?.status ?? 'Unknown'),
    checks,
    isLoading,
    isError,
    refetch,
  };
}
