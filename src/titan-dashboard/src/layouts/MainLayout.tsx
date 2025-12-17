import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import './MainLayout.css';

export function MainLayout() {
  const { user, logout, hasRole } = useAuth();
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  return (
    <div className="layout">
      <aside className="sidebar">
        <div className="sidebar-header">
          <span className="logo">⚔️</span>
          <span className="logo-text">Titan Admin</span>
        </div>
        
        <nav className="sidebar-nav">
          <NavLink to="/" end className="nav-link">
            <span className="nav-icon">🏠</span>
            <span>Home</span>
          </NavLink>
          
          <NavLink to="/players" className="nav-link">
            <span className="nav-icon">👥</span>
            <span>Players</span>
          </NavLink>
          
          <NavLink to="/base-types" className="nav-link">
            <span className="nav-icon">📦</span>
            <span>Item Types</span>
          </NavLink>
          
          <NavLink to="/seasons" className="nav-link">
            <span className="nav-icon">📅</span>
            <span>Seasons</span>
          </NavLink>
          
          {hasRole('SuperAdmin') && (
            <>
              <div className="nav-divider" />
              
              <NavLink to="/admin-users" className="nav-link">
                <span className="nav-icon">🛡️</span>
                <span>Admin Users</span>
              </NavLink>
              
              <NavLink to="/rate-limiting" className="nav-link">
                <span className="nav-icon">⚡</span>
                <span>Rate Limiting</span>
              </NavLink>
              
              <NavLink to="/rate-limiting/metrics" className="nav-link nav-link-sub">
                <span className="nav-icon">📊</span>
                <span>RL Metrics</span>
              </NavLink>
            </>
          )}
        </nav>
      </aside>
      
      <div className="main-container">
        <header className="header">
          <div className="header-left">
            {/* Breadcrumb or search could go here */}
          </div>
          
          <div className="header-right">
            <div className="user-info">
              <span className="user-avatar">👤</span>
              <div className="user-details">
                <span className="user-name">{user?.displayName || user?.email}</span>
                <span className="user-role">{user?.roles[0] || 'User'}</span>
              </div>
            </div>
            
            <button onClick={handleLogout} className="btn btn-ghost btn-sm">
              Sign Out
            </button>
          </div>
        </header>
        
        <main className="main-content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
