import { useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { 
  Sword, 
  Home, 
  Users, 
  Package, 
  Calendar, 
  Shield, 
  Zap, 
  BarChart3, 
  User,
  LogOut,
  Menu,
  X
} from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import './MainLayout.css';

export function MainLayout() {
  const { user, logout, hasRole } = useAuth();
  const navigate = useNavigate();
  const [mobileNavOpen, setMobileNavOpen] = useState(false);

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const closeMobileNav = () => setMobileNavOpen(false);

  return (
    <div className="layout">
      <aside className={`sidebar ${mobileNavOpen ? 'open' : ''}`}>
        <div className="sidebar-header">
          <Sword className="logo-icon" size={24} />
          <span className="logo-text">Titan Admin</span>
        </div>
        
        <nav className="sidebar-nav">
          <NavLink to="/" end className="nav-link" onClick={closeMobileNav}>
            <Home size={18} />
            <span>Home</span>
          </NavLink>
          
          <NavLink to="/players" className="nav-link" onClick={closeMobileNav}>
            <Users size={18} />
            <span>Players</span>
          </NavLink>
          
          <NavLink to="/base-types" className="nav-link" onClick={closeMobileNav}>
            <Package size={18} />
            <span>Item Types</span>
          </NavLink>
          
          <NavLink to="/seasons" className="nav-link" onClick={closeMobileNav}>
            <Calendar size={18} />
            <span>Seasons</span>
          </NavLink>
          
          {hasRole('SuperAdmin') && (
            <>
              <div className="nav-divider" />
              
              <NavLink to="/admin-users" className="nav-link" onClick={closeMobileNav}>
                <Shield size={18} />
                <span>Admin Users</span>
              </NavLink>
              
              <NavLink to="/rate-limiting" className="nav-link" onClick={closeMobileNav}>
                <Zap size={18} />
                <span>Rate Limiting</span>
              </NavLink>
              
              <NavLink to="/rate-limiting/metrics" className="nav-link nav-link-sub" onClick={closeMobileNav}>
                <BarChart3 size={18} />
                <span>RL Metrics</span>
              </NavLink>
            </>
          )}
        </nav>
      </aside>
      
      {/* Mobile overlay */}
      {mobileNavOpen && (
        <div className="mobile-overlay" onClick={closeMobileNav} />
      )}
      
      <div className="main-container">
        <header className="header">
          <div className="header-left">
            <button 
              className="mobile-nav-toggle"
              onClick={() => setMobileNavOpen(!mobileNavOpen)}
              aria-label="Toggle navigation"
              aria-expanded={mobileNavOpen}
            >
              {mobileNavOpen ? <X size={24} /> : <Menu size={24} />}
            </button>
          </div>
          
          <div className="header-right">
            <div className="user-info">
              <span className="user-avatar">
                <User size={18} />
              </span>
              <div className="user-details">
                <span className="user-name">{user?.displayName || user?.email}</span>
                <span className="user-role">{user?.roles[0] || 'User'}</span>
              </div>
            </div>
            
            <button onClick={handleLogout} className="btn btn-ghost btn-sm">
              <LogOut size={16} />
              <span className="logout-text">Sign Out</span>
            </button>
          </div>
        </header>
        
        <main className="main-content" id="main-content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
