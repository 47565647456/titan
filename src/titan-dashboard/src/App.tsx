import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { MainLayout } from './layouts/MainLayout';
import { LoginPage } from './pages/Login';
import { HomePage } from './pages/Home';
import { PlayersPage } from './pages/Players';
import { SeasonsPage } from './pages/Seasons';
import { BaseTypesPage } from './pages/BaseTypes';
import { AdminUsersPage } from './pages/AdminUsers';
import { RateLimitingPage } from './pages/RateLimiting';
import { RateLimitingMetricsPage } from './pages/RateLimitingMetrics';
import { SessionsPage } from './pages/Sessions';
import { BroadcastingPage } from './pages/Broadcasting';
import './index.css';


const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30000,
      retry: 1,
    },
  },
});

function ProtectedRoute({ children, requiredRole }: { children: React.ReactNode; requiredRole?: string }) {
  const { isAuthenticated, isLoading, hasRole } = useAuth();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center" style={{ minHeight: '100vh' }}>
        <span className="spinner" />
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  if (requiredRole && !hasRole(requiredRole)) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      
      <Route
        element={
          <ProtectedRoute>
            <MainLayout />
          </ProtectedRoute>
        }
      >
        <Route path="/" element={<HomePage />} />
        <Route path="/players" element={<PlayersPage />} />
        <Route path="/seasons" element={<SeasonsPage />} />
        <Route path="/base-types" element={<BaseTypesPage />} />
        
        {/* SuperAdmin only routes */}
        <Route
          path="/admin-users"
          element={
            <ProtectedRoute requiredRole="SuperAdmin">
              <AdminUsersPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/rate-limiting"
          element={
            <ProtectedRoute requiredRole="SuperAdmin">
              <RateLimitingPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/rate-limiting/metrics"
          element={
            <ProtectedRoute requiredRole="SuperAdmin">
              <RateLimitingMetricsPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/sessions"
          element={
            <ProtectedRoute requiredRole="SuperAdmin">
              <SessionsPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/broadcasting"
          element={
            <ProtectedRoute requiredRole="SuperAdmin">
              <BroadcastingPage />
            </ProtectedRoute>
          }
        />
      </Route>
      
      {/* Catch all - redirect to home */}
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <AppRoutes />
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}
