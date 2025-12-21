import { useState, useEffect, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { sessionsApi } from '../api/client';
import { RefreshCw, Key, Trash2, Users, AlertCircle } from 'lucide-react';
import type { SessionInfo } from '../types';
import './DataPage.css';
import './Sessions.css';

export function SessionsPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);
  const [showInvalidateConfirm, setShowInvalidateConfirm] = useState(false);
  const [invalidateTarget, setInvalidateTarget] = useState<SessionInfo | null>(null);
  const [userIdFilter, setUserIdFilter] = useState('');
  const [invalidateError, setInvalidateError] = useState<string | null>(null);
  const pageSize = 25;

  const { data: sessionsData, isLoading, refetch, isFetching } = useQuery({
    queryKey: ['sessions', page, pageSize],
    queryFn: () => sessionsApi.getAll(page * pageSize, pageSize),
    refetchInterval: 30000, // Auto-refresh every 30 seconds
  });

  const { data: countData } = useQuery({
    queryKey: ['sessionCount'],
    queryFn: sessionsApi.getCount,
    refetchInterval: 30000,
  });

  const invalidateMutation = useMutation({
    mutationFn: sessionsApi.invalidate,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sessions'] });
      queryClient.invalidateQueries({ queryKey: ['sessionCount'] });
      setShowInvalidateConfirm(false);
      setInvalidateTarget(null);
      setInvalidateError(null);
    },
    onError: (error: Error) => {
      setInvalidateError(error.message || 'Failed to invalidate session');
    },
  });

  // Handle Escape key to close modal
  const handleKeyDown = useCallback((e: KeyboardEvent) => {
    if (e.key === 'Escape' && showInvalidateConfirm) {
      setShowInvalidateConfirm(false);
      setInvalidateError(null);
    }
  }, [showInvalidateConfirm]);

  useEffect(() => {
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [handleKeyDown]);

  const formatDateTime = (dateString: string) => {
    return new Date(dateString).toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  const getTimeRemaining = (expiresAt: string) => {
    const now = new Date();
    const expiry = new Date(expiresAt);
    const diffMs = expiry.getTime() - now.getTime();
    
    if (diffMs <= 0) return 'Expired';
    
    const diffMins = Math.floor(diffMs / 60000);
    if (diffMins < 60) return `${diffMins}m`;
    
    const diffHours = Math.floor(diffMins / 60);
    const remainingMins = diffMins % 60;
    return `${diffHours}h ${remainingMins}m`;
  };

  const isExpiringSoon = (expiresAt: string) => {
    const now = new Date();
    const expiry = new Date(expiresAt);
    const diffMs = expiry.getTime() - now.getTime();
    return diffMs > 0 && diffMs < 10 * 60 * 1000; // Less than 10 minutes
  };

  const openInvalidateConfirm = (session: SessionInfo) => {
    setInvalidateTarget(session);
    setShowInvalidateConfirm(true);
    setInvalidateError(null);
  };

  const closeModal = () => {
    setShowInvalidateConfirm(false);
    setInvalidateError(null);
  };

  const handleInvalidate = () => {
    if (invalidateTarget) {
      invalidateMutation.mutate(invalidateTarget.ticketId);
    }
  };

  // Filter sessions on current page (note: this only filters the current page)
  const filteredSessions = sessionsData?.sessions.filter(session => {
    if (!userIdFilter) return true;
    return session.userId.toLowerCase().includes(userIdFilter.toLowerCase());
  }) ?? [];

  // Admin count from backend total, not current page
  const adminCount = sessionsData?.sessions.filter(s => s.isAdmin).length ?? 0;
  const totalCount = countData?.count ?? sessionsData?.totalCount ?? 0;
  const totalPages = Math.ceil((sessionsData?.totalCount ?? 0) / pageSize);

  return (
    <div className="data-page">
      <div className="page-header">
        <div>
          <h1><Key size={24} className="page-icon" /> Sessions</h1>
          <p className="subtitle">View and manage active user sessions</p>
        </div>
        <button 
          type="button"
          className="btn btn-secondary" 
          onClick={() => refetch()}
          disabled={isFetching}
        >
          <RefreshCw size={16} className={isFetching ? 'spinning' : ''} />
          Refresh
        </button>
      </div>

      {/* Stats Cards */}
      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-icon">
            <Users size={20} />
          </div>
          <div className="stat-content">
            <span className="stat-value">{totalCount}</span>
            <span className="stat-label">Active Sessions</span>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-icon stat-icon-admin">
            <Key size={20} />
          </div>
          <div className="stat-content">
            <span className="stat-value">{adminCount}</span>
            <span className="stat-label">Admin Sessions (this page)</span>
          </div>
        </div>
      </div>

      {/* Filter - with accessibility label */}
      <div className="filter-bar">
        <label htmlFor="userIdFilter" className="visually-hidden">Filter by User ID</label>
        <input
          id="userIdFilter"
          type="text"
          className="input filter-input"
          placeholder="Filter by User ID (current page only)..."
          aria-label="Filter sessions by User ID"
          value={userIdFilter}
          onChange={(e) => setUserIdFilter(e.target.value)}
        />
      </div>

      {isLoading ? (
        <div className="loading-state">
          <span className="spinner" />
          <span>Loading sessions...</span>
        </div>
      ) : !filteredSessions.length ? (
        <div className="empty-state">
          <p>No active sessions found.</p>
        </div>
      ) : (
        <div className="card">
          <div className="card-body" style={{ padding: 0 }}>
            <table className="table">
              <thead>
                <tr>
                  <th>User ID</th>
                  <th>Provider</th>
                  <th>Roles</th>
                  <th>Created</th>
                  <th>Expires In</th>
                  <th>Last Activity</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filteredSessions.map((session) => (
                  <tr key={session.ticketId} className={session.isAdmin ? 'row-admin' : ''}>
                    <td className="cell-mono">{session.userId.slice(0, 8)}...</td>
                    <td>
                      <span className={`badge badge-provider badge-${session.provider.toLowerCase()}`}>
                        {session.provider}
                      </span>
                      {session.isAdmin && (
                        <span className="badge badge-admin" style={{ marginLeft: '4px' }}>
                          Admin
                        </span>
                      )}
                    </td>
                    <td>
                      <div className="role-badges">
                        {session.roles.slice(0, 2).map((role) => (
                          <span key={role} className="badge badge-secondary">
                            {role}
                          </span>
                        ))}
                        {session.roles.length > 2 && (
                          <span className="badge badge-secondary">+{session.roles.length - 2}</span>
                        )}
                      </div>
                    </td>
                    <td>{formatDateTime(session.createdAt)}</td>
                    <td>
                      <span className={isExpiringSoon(session.expiresAt) ? 'expiring-soon' : ''}>
                        {getTimeRemaining(session.expiresAt)}
                      </span>
                    </td>
                    <td>{formatDateTime(session.lastActivityAt)}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-sm btn-danger"
                        onClick={() => openInvalidateConfirm(session)}
                        title="Invalidate session"
                        aria-label={`Invalidate session for user ${session.userId.slice(0, 8)}`}
                      >
                        <Trash2 size={14} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="pagination">
              <button
                type="button"
                className="btn btn-sm btn-secondary"
                onClick={() => setPage(p => Math.max(0, p - 1))}
                disabled={page === 0}
                aria-label="Go to previous page"
              >
                Previous
              </button>
              <span className="page-info">
                Page {page + 1} of {totalPages}
              </span>
              <button
                type="button"
                className="btn btn-sm btn-secondary"
                onClick={() => setPage(p => p + 1)}
                disabled={page >= totalPages - 1}
                aria-label="Go to next page"
              >
                Next
              </button>
            </div>
          )}
        </div>
      )}

      {/* Invalidate Confirmation Modal with ARIA attributes */}
      {showInvalidateConfirm && invalidateTarget && (
        <div 
          className="modal-overlay" 
          onClick={closeModal}
          role="presentation"
        >
          <div 
            className="modal modal-small" 
            onClick={(e) => e.stopPropagation()}
            role="dialog"
            aria-modal="true"
            aria-labelledby="invalidate-modal-title"
            aria-describedby="invalidate-modal-description"
          >
            <div className="modal-header">
              <h2 id="invalidate-modal-title" className="modal-title">⚠️ Invalidate Session</h2>
            </div>
            <div className="modal-body">
              <p id="invalidate-modal-description">Are you sure you want to invalidate this session?</p>
              <p className="text-muted">
                User ID: <code>{invalidateTarget.userId.slice(0, 8)}...</code>
                <br />
                Provider: <strong>{invalidateTarget.provider}</strong>
              </p>
              <p className="text-muted">The user will be logged out immediately.</p>
              
              {/* Error message display */}
              {invalidateError && (
                <div className="alert alert-error" role="alert">
                  <AlertCircle size={16} />
                  <span>{invalidateError}</span>
                </div>
              )}
            </div>
            <div className="modal-footer">
              <button
                type="button"
                className="btn btn-secondary"
                onClick={closeModal}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-danger"
                onClick={handleInvalidate}
                disabled={invalidateMutation.isPending}
              >
                {invalidateMutation.isPending ? 'Invalidating...' : 'Invalidate'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
