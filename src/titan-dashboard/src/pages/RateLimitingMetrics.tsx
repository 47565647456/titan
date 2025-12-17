import { useQuery } from '@tanstack/react-query';
import { rateLimitingApi } from '../api/client';
import { useAdminMetrics } from '../hooks/useAdminMetrics';
import './DataPage.css';
import './RateLimitingMetrics.css';

export function RateLimitingMetricsPage() {
  // Use SignalR for real-time metrics (replaces polling)
  const { metrics, connectionState, refresh, clearTimeout, clearBucket, error: signalRError } = useAdminMetrics();
  
  // Keep config as regular query (doesn't change frequently)
  const { data: config } = useQuery({
    queryKey: ['rateLimitConfig'],
    queryFn: rateLimitingApi.getConfig,
  });

  const isLoading = connectionState === 'connecting' && !metrics;

  const formatDuration = (seconds: number): string => {
    if (seconds >= 3600) return `${Math.floor(seconds / 3600)}h ${Math.floor((seconds % 3600) / 60)}m`;
    if (seconds >= 60) return `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
    return `${seconds}s`;
  };

  const truncateId = (id: string): string => {
    // If it's a GUID-like string, truncate it
    if (id.length > 20 && id.includes('-')) {
      return id.substring(0, 8) + '...';
    }
    return id;
  };

  // Group buckets by partition key
  const groupedBuckets = metrics?.buckets.reduce((acc, bucket) => {
    if (!acc[bucket.partitionKey]) {
      acc[bucket.partitionKey] = [];
    }
    acc[bucket.partitionKey].push(bucket);
    return acc;
  }, {} as Record<string, typeof metrics.buckets>) || {};

  // Group timeouts by partition key
  const groupedTimeouts = metrics?.timeouts.reduce((acc, timeout) => {
    if (!acc[timeout.partitionKey]) {
      acc[timeout.partitionKey] = [];
    }
    acc[timeout.partitionKey].push(timeout);
    return acc;
  }, {} as Record<string, typeof metrics.timeouts>) || {};

  if (isLoading) {
    return (
      <div className="data-page">
        <div className="loading-state">
          <span className="spinner" />
          <span>Connecting to metrics stream...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="data-page rate-limit-metrics-page">
      <div className="page-header">
        <div>
          <h1>📊 Rate Limiting Metrics</h1>
          <p className="subtitle">Live view of active rate limit buckets and timeouts</p>
        </div>
        <div className="page-actions">
          <button className="btn btn-secondary" onClick={refresh} disabled={connectionState !== 'connected'}>
            🔄 Refresh
          </button>
        </div>
      </div>

      {/* Summary Cards */}
      <div className="metrics-summary">
        <div className="summary-card">
          <div className="summary-icon">📦</div>
          <div className="summary-content">
            <div className="summary-value">{metrics?.activeBuckets || 0}</div>
            <div className="summary-label">Active Buckets</div>
          </div>
        </div>
        <div className="summary-card timeout-card">
          <div className="summary-icon">⏱️</div>
          <div className="summary-content">
            <div className="summary-value">{metrics?.activeTimeouts || 0}</div>
            <div className="summary-label">Active Timeouts</div>
          </div>
        </div>
        <div className="summary-card status-card">
          <div className="summary-icon">{config?.enabled ? '✅' : '⚠️'}</div>
          <div className="summary-content">
            <div className="summary-value">{config?.enabled ? 'Enabled' : 'Disabled'}</div>
            <div className="summary-label">Rate Limiting Status</div>
          </div>
        </div>
        <div className="summary-card">
          <div className="summary-icon">📋</div>
          <div className="summary-content">
            <div className="summary-value">{config?.policies.length || 0}</div>
            <div className="summary-label">Configured Policies</div>
          </div>
        </div>
      </div>

      {/* Active Timeouts Section */}
      {metrics && metrics.timeouts.length > 0 && (
        <div className="metrics-section">
          <h2>🚫 Active Timeouts ({metrics.timeouts.length})</h2>
          <p className="text-muted">Clients currently in timeout - all their requests are being denied</p>
          <div className="card">
            <div className="card-body" style={{ padding: 0 }}>
              <table className="table">
                <thead>
                  <tr>
                    <th>Partition Key</th>
                    <th>Policy</th>
                    <th>Time Remaining</th>
                    <th>Progress</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {metrics.timeouts.map((timeout, idx) => {
                    const policy = config?.policies.find(p => p.name === timeout.policyName);
                    const maxTimeout = policy?.rules[0]?.timeoutSeconds || 300;
                    const progress = ((maxTimeout - timeout.secondsRemaining) / maxTimeout) * 100;
                    
                    return (
                      <tr key={idx} className="timeout-row">
                        <td>
                          <code title={timeout.partitionKey}>{truncateId(timeout.partitionKey)}</code>
                        </td>
                        <td>
                          <span className="badge badge-error">{timeout.policyName}</span>
                        </td>
                        <td className="timeout-time">
                          <span className="countdown">{formatDuration(timeout.secondsRemaining)}</span>
                        </td>
                        <td>
                          <div className="progress-bar">
                            <div 
                              className="progress-fill timeout-progress" 
                              style={{ width: `${progress}%` }}
                            />
                          </div>
                        </td>
                        <td>
                          <button
                            className="btn btn-sm btn-danger"
                            onClick={() => clearTimeout(timeout.partitionKey, timeout.policyName)}
                            title="End this timeout early"
                          >
                            End Timeout
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* Active Buckets Section */}
      <div className="metrics-section">
        <h2>📦 Active Rate Limit Buckets ({metrics?.activeBuckets || 0})</h2>
        <p className="text-muted">Current request counts per partition key and policy</p>
        
        {Object.keys(groupedBuckets).length === 0 ? (
          <div className="empty-state">
            <p>No active rate limit buckets. Make some requests to see data here.</p>
          </div>
        ) : (
          <div className="buckets-grid">
            {Object.entries(groupedBuckets).map(([partitionKey, buckets]) => (
              <div key={partitionKey} className="bucket-card">
                <div className="bucket-header">
                  <code title={partitionKey}>{truncateId(partitionKey)}</code>
                  <div className="bucket-header-actions">
                    {groupedTimeouts[partitionKey] && (
                      <span className="badge badge-error">TIMEOUT</span>
                    )}
                    <button
                      className="btn btn-xs btn-ghost"
                      onClick={() => clearBucket(partitionKey)}
                      title="Clear request count"
                    >
                      ✕
                    </button>
                  </div>
                </div>
                <div className="bucket-rules">
                  {buckets.map((bucket, idx) => {
                    const policy = config?.policies.find(p => p.name === bucket.policyName);
                    const maxHits = policy?.rules.find(r => r.periodSeconds === bucket.periodSeconds)?.maxHits || 100;
                    const usagePercent = Math.min((bucket.currentCount / maxHits) * 100, 100);
                    const isNearLimit = usagePercent >= 80;
                    
                    return (
                      <div key={idx} className="bucket-rule">
                        <div className="rule-header">
                          <span className="badge badge-primary">{bucket.policyName}</span>
                          <span className="rule-period">{formatDuration(bucket.periodSeconds)} window</span>
                        </div>
                        <div className="rule-stats">
                          <span className={`rule-count ${isNearLimit ? 'near-limit' : ''}`}>
                            {bucket.currentCount} / {maxHits}
                          </span>
                          <span className="rule-remaining">
                            resets in {formatDuration(bucket.secondsRemaining)}
                          </span>
                        </div>
                        <div className="progress-bar">
                          <div 
                            className={`progress-fill ${isNearLimit ? 'warning' : ''}`} 
                            style={{ width: `${usagePercent}%` }}
                          />
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Connection status indicator */}
      <div className="auto-refresh-indicator">
        {connectionState === 'connected' ? (
          <>
            <span className="pulse" />
            <span>Connected - Real-time updates</span>
          </>
        ) : connectionState === 'connecting' ? (
          <>
            <span className="spinner" style={{ width: 8, height: 8 }} />
            <span>Connecting...</span>
          </>
        ) : signalRError ? (
          <span style={{ color: 'var(--color-error)' }}>⚠️ {signalRError}</span>
        ) : (
          <span>Disconnected</span>
        )}
      </div>
    </div>
  );
}
