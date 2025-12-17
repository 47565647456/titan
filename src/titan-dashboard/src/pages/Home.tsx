import { Link } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import './Home.css';

export function HomePage() {
  const { user, hasRole } = useAuth();

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
        </div>
        <div className="card-body">
          <div className="status-grid">
            <div className="status-item">
              <span className="status-indicator status-ok" />
              <span>Orleans Cluster</span>
            </div>
            <div className="status-item">
              <span className="status-indicator status-ok" />
              <span>Database</span>
            </div>
            <div className="status-item">
              <span className="status-indicator status-ok" />
              <span>Redis</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
