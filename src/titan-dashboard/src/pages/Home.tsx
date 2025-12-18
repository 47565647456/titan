import { Link } from 'react-router-dom';
import { RefreshCw } from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { useHealthCheck } from '../hooks/useHealthCheck';
import './Home.css';

export function HomePage() {
  const { user, hasRole } = useAuth();
  const { overallStatus, checks, isLoading, isError, refetch } = useHealthCheck();

  /** Map health status to CSS class */
  const getStatusClass = (status: string): string => {
    switch (status) {
      case 'Healthy':
        return 'status-ok';
      case 'Degraded':
        return 'status-warning';
      case 'Unhealthy':
        return 'status-error';
      default:
        return 'status-warning';
    }
  };

  return (
    <div className="home">
      <div className="home-header">
        <h1>Welcome back, {user?.displayName || user?.email?.split('@')[0]}</h1>
        <p className="text-muted">Titan Admin Dashboard</p>
      </div>

      <div className="quick-links">
        <Link to="/players" className="quick-link-card">
          <span className="quick-link-icon">👥</span>
          <div className="quick-link-content">
            <h3>Players</h3>
            <p>Manage player accounts and characters</p>
          </div>
          <span className="quick-link-arrow">→</span>
        </Link>

        <Link to="/base-types" className="quick-link-card">
          <span className="quick-link-icon">📦</span>
          <div className="quick-link-content">
            <h3>Item Types</h3>
            <p>Configure base item types and properties</p>
          </div>
          <span className="quick-link-arrow">→</span>
        </Link>

        <Link to="/seasons" className="quick-link-card">
          <span className="quick-link-icon">📅</span>
          <div className="quick-link-content">
            <h3>Seasons</h3>
            <p>Manage game seasons and migrations</p>
          </div>
          <span className="quick-link-arrow">→</span>
        </Link>

        {hasRole('SuperAdmin') && (
          <>
            <Link to="/admin-users" className="quick-link-card">
              <span className="quick-link-icon">🛡️</span>
              <div className="quick-link-content">
                <h3>Admin Users</h3>
                <p>Manage dashboard administrators</p>
              </div>
              <span className="quick-link-arrow">→</span>
            </Link>

            <Link to="/rate-limiting" className="quick-link-card">
              <span className="quick-link-icon">⚡</span>
              <div className="quick-link-content">
                <h3>Rate Limiting</h3>
                <p>Configure API rate limit policies</p>
              </div>
              <span className="quick-link-arrow">→</span>
            </Link>
          </>
        )}
      </div>

      <div className="system-status card">
        <div className="card-header">
          <h2>System Status</h2>
          <button 
            className="btn btn-sm btn-ghost" 
            onClick={() => refetch()}
            disabled={isLoading}
            title="Refresh status"
          >
            <RefreshCw size={14} />
          </button>
        </div>
        <div className="card-body">
          {isLoading && checks.length === 0 ? (
            <div className="status-loading">Checking system health...</div>
          ) : checks.length === 0 && isError ? (
            <div className="status-error-message">
              Unable to fetch health status
            </div>
          ) : (
            <>
              {isError && (
                <div className="status-error-message status-error-message-spaced">
                  ⚠️ Connection issue - showing last known status
                </div>
              )}
              <div className="status-overall">
                <span className={`status-indicator ${getStatusClass(overallStatus)}`} />
                <span className="status-overall-text">
                  Overall: {overallStatus}
                </span>
              </div>
              <div className="status-grid">
                {checks.map((check) => (
                  <div key={check.name} className="status-item">
                    <span className={`status-indicator ${getStatusClass(check.status)}`} />
                    <span>{check.displayName}</span>
                    <span className="status-duration">{check.duration}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

