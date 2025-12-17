import { createContext, useContext, useState, useEffect, useRef, useCallback, type ReactNode } from 'react';
import { authApi, type RefreshResponse } from '../api/client';
import type { AdminUser, LoginRequest } from '../types';

interface AuthContextType {
  user: AdminUser | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  login: (data: LoginRequest) => Promise<void>;
  logout: () => Promise<void>;
  hasRole: (role: string) => boolean;
  hasAnyRole: (roles: string[]) => boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

// Refresh at 80% of token lifetime
const REFRESH_THRESHOLD = 0.8;

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AdminUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const refreshTimerRef = useRef<number | null>(null);

  // Schedule next token refresh
  const scheduleRefresh = useCallback((expiresInSeconds: number) => {
    // Clear any existing timer
    if (refreshTimerRef.current) {
      clearTimeout(refreshTimerRef.current);
    }

    // Calculate when to refresh (80% of token lifetime)
    const refreshTimeMs = expiresInSeconds * 1000 * REFRESH_THRESHOLD;
    
    console.log(`[Auth] Scheduling token refresh in ${Math.round(refreshTimeMs / 1000)}s`);

    refreshTimerRef.current = window.setTimeout(async () => {
      try {
        console.log('[Auth] Proactively refreshing token...');
        const response = await authApi.refresh();
        
        if (response.success) {
          // Update localStorage
          localStorage.setItem('accessToken', response.accessToken);
          localStorage.setItem('refreshToken', response.refreshToken);
          localStorage.setItem('tokenExpiry', 
            (Date.now() + response.expiresInSeconds * 1000).toString());
          
          // Schedule next refresh
          scheduleRefresh(response.expiresInSeconds);
          console.log('[Auth] Token refreshed successfully');
        }
      } catch (error) {
        console.error('[Auth] Token refresh failed:', error);
        // Clear auth state on refresh failure
        localStorage.removeItem('accessToken');
        localStorage.removeItem('refreshToken');
        localStorage.removeItem('tokenExpiry');
        localStorage.removeItem('user');
        setUser(null);
        // Redirect will happen via axios interceptor on next API call
      }
    }, refreshTimeMs);
  }, []);

  // Check for existing session on mount
  useEffect(() => {
    const token = localStorage.getItem('accessToken');
    const savedUser = localStorage.getItem('user');
    const tokenExpiry = localStorage.getItem('tokenExpiry');
    
    if (token && savedUser) {
      try {
        const parsedUser = JSON.parse(savedUser);
        setUser(parsedUser);
        
        // If we have expiry info, schedule the next refresh
        if (tokenExpiry) {
          const expiryTime = parseInt(tokenExpiry, 10);
          const remainingMs = expiryTime - Date.now();
          
          if (remainingMs > 0) {
            // Convert remaining time to a full token lifetime for scheduling
            // (we don't know the original expiry, so estimate)
            const remainingSeconds = Math.floor(remainingMs / 1000);
            scheduleRefresh(remainingSeconds / REFRESH_THRESHOLD);
          } else {
            // Token expired, try to refresh immediately
            authApi.refresh()
              .then((response: RefreshResponse) => {
                if (response.success) {
                  localStorage.setItem('accessToken', response.accessToken);
                  localStorage.setItem('refreshToken', response.refreshToken);
                  localStorage.setItem('tokenExpiry', 
                    (Date.now() + response.expiresInSeconds * 1000).toString());
                  scheduleRefresh(response.expiresInSeconds);
                }
              })
              .catch(() => {
                // Refresh failed, clear auth
                localStorage.removeItem('accessToken');
                localStorage.removeItem('refreshToken');
                localStorage.removeItem('tokenExpiry');
                localStorage.removeItem('user');
                setUser(null);
              });
          }
        }
      } catch {
        localStorage.removeItem('accessToken');
        localStorage.removeItem('refreshToken');
        localStorage.removeItem('tokenExpiry');
        localStorage.removeItem('user');
      }
    }
    setIsLoading(false);

    // Cleanup timer on unmount
    return () => {
      if (refreshTimerRef.current) {
        clearTimeout(refreshTimerRef.current);
      }
    };
  }, [scheduleRefresh]);

  const login = async (data: LoginRequest) => {
    const response = await authApi.login(data);
    
    if (response.success && response.accessToken) {
      // Store tokens in localStorage (cookies are also set by server)
      localStorage.setItem('accessToken', response.accessToken);
      localStorage.setItem('refreshToken', response.refreshToken);
      localStorage.setItem('tokenExpiry', 
        (Date.now() + response.expiresInSeconds * 1000).toString());
      
      const userData: AdminUser = {
        id: response.userId,
        email: response.email,
        displayName: response.displayName,
        roles: response.roles,
        createdAt: new Date().toISOString(),
        lastLoginAt: new Date().toISOString(),
      };
      
      localStorage.setItem('user', JSON.stringify(userData));
      setUser(userData);
      
      // Schedule token refresh at 80% of lifetime
      scheduleRefresh(response.expiresInSeconds);
    } else {
      throw new Error('Login failed');
    }
  };

  const logout = async () => {
    // Clear refresh timer
    if (refreshTimerRef.current) {
      clearTimeout(refreshTimerRef.current);
      refreshTimerRef.current = null;
    }

    try {
      await authApi.logout();
    } catch {
      // Ignore logout errors
    } finally {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('tokenExpiry');
      localStorage.removeItem('user');
      setUser(null);
    }
  };

  const hasRole = (role: string): boolean => {
    return user?.roles.some(r => r.toLowerCase() === role.toLowerCase()) ?? false;
  };

  const hasAnyRole = (roles: string[]): boolean => {
    return roles.some(role => hasRole(role));
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        isLoading,
        isAuthenticated: !!user,
        login,
        logout,
        hasRole,
        hasAnyRole,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
