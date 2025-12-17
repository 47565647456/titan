import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { accountsApi } from '../api/client';
import type { AccountSummary } from '../types';
import './DataPage.css';
import './Players.css';

type TabType = 'list' | 'search';

export function PlayersPage() {
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState<TabType>('list');
  const [selectedAccountId, setSelectedAccountId] = useState<string | null>(null);
  const [searchId, setSearchId] = useState('');
  const [hasSearched, setHasSearched] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<AccountSummary | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const { data: accounts, isLoading, refetch } = useQuery({
    queryKey: ['accounts'],
    queryFn: accountsApi.getAll,
  });

  const { data: accountDetail } = useQuery({
    queryKey: ['account', selectedAccountId],
    queryFn: () => accountsApi.getById(selectedAccountId!),
    enabled: !!selectedAccountId,
  });

  const { data: characters } = useQuery({
    queryKey: ['characters', selectedAccountId],
    queryFn: () => accountsApi.getCharacters(selectedAccountId!),
    enabled: !!selectedAccountId,
  });

  const createMutation = useMutation({
    mutationFn: accountsApi.create,
    onSuccess: (newAccount) => {
      queryClient.invalidateQueries({ queryKey: ['accounts'] });
      setSuccessMessage(`Account created successfully! ID: ${newAccount.accountId}`);
      setSelectedAccountId(newAccount.accountId);
      setActiveTab('search');
      setSearchId(newAccount.accountId);
      setTimeout(() => setSuccessMessage(null), 5000);
    },
    onError: (err: Error) => {
      setErrorMessage(`Failed to create account: ${err.message}`);
      setTimeout(() => setErrorMessage(null), 5000);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: accountsApi.delete,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['accounts'] });
      setSelectedAccountId(null);
      setShowDeleteConfirm(false);
      setDeleteTarget(null);
      setSuccessMessage('Account deleted successfully.');
      setTimeout(() => setSuccessMessage(null), 5000);
    },
    onError: (err: Error) => {
      setErrorMessage(`Failed to delete account: ${err.message}`);
      setTimeout(() => setErrorMessage(null), 5000);
    },
  });

  const handleSearch = () => {
    if (!searchId.trim()) {
      setErrorMessage('Please enter an Account ID');
      setTimeout(() => setErrorMessage(null), 5000);
      return;
    }
    
    // Basic GUID validation
    const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!guidRegex.test(searchId.trim())) {
      setErrorMessage('Invalid Account ID format. Please enter a valid GUID.');
      setTimeout(() => setErrorMessage(null), 5000);
      return;
    }
    
    setHasSearched(true);
    setSelectedAccountId(searchId.trim());
  };

  const viewAccount = (accountId: string) => {
    setActiveTab('search');
    setSearchId(accountId);
    setSelectedAccountId(accountId);
    setHasSearched(true);
  };

  const openDeleteConfirm = (account: AccountSummary) => {
    setDeleteTarget(account);
    setShowDeleteConfirm(true);
  };

  const getRestrictionBadges = (restrictions: string): string[] => {
    const badges: string[] = [];
    if (restrictions.includes('Hardcore')) badges.push('Hardcore');
    if (restrictions.includes('SoloSelfFound') || restrictions.includes('SSF')) badges.push('SSF');
    return badges;
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  };

  const formatDateTime = (dateString: string) => {
    return new Date(dateString).toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  return (
    <div className="data-page players-page">
      <div className="page-header">
        <div>
          <h1>👥 Players</h1>
          <p className="subtitle">View and manage player accounts</p>
        </div>
      </div>

      {/* Tabs */}
      <div className="tabs">
        <button 
          className={`tab ${activeTab === 'list' ? 'active' : ''}`}
          onClick={() => setActiveTab('list')}
        >
          📋 All Accounts
        </button>
        <button 
          className={`tab ${activeTab === 'search' ? 'active' : ''}`}
          onClick={() => setActiveTab('search')}
        >
          🔍 Search
        </button>
      </div>

      {/* Alert Messages */}
      {errorMessage && <div className="alert alert-danger">{errorMessage}</div>}
      {successMessage && <div className="alert alert-success">{successMessage}</div>}

      {/* All Accounts Tab */}
      {activeTab === 'list' && (
        <>
          <div className="actions-bar">
            <button
              className="btn btn-primary"
              onClick={() => createMutation.mutate()}
              disabled={createMutation.isPending}
            >
              {createMutation.isPending ? 'Creating...' : '➕ Create Account'}
            </button>
            <button 
              className="btn btn-secondary"
              onClick={() => refetch()}
              disabled={isLoading}
            >
              🔄 Refresh
            </button>
          </div>

          {isLoading ? (
            <div className="loading-state">
              <span className="spinner" />
              <span>Loading accounts...</span>
            </div>
          ) : !accounts || accounts.length === 0 ? (
            <div className="empty-state">
              <p>No accounts found in the database.</p>
            </div>
          ) : (
            <>
              <div className="card">
                <div className="card-body" style={{ padding: 0 }}>
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Account ID</th>
                        <th>Last Modified</th>
                        <th>Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {accounts.map((account) => (
                        <tr key={account.accountId}>
                          <td><code>{account.accountId}</code></td>
                          <td>{formatDateTime(account.lastModified)}</td>
                          <td>
                            <div className="action-buttons">
                              <button 
                                className="btn btn-sm btn-secondary"
                                onClick={() => viewAccount(account.accountId)}
                              >
                                View
                              </button>
                              <button 
                                className="btn btn-sm btn-danger"
                                onClick={() => openDeleteConfirm(account)}
                              >
                                Delete
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
              <p className="text-muted">Showing {accounts.length} accounts (max 1000)</p>
            </>
          )}
        </>
      )}

      {/* Search Tab */}
      {activeTab === 'search' && (
        <>
          <div className="search-section">
            <div className="search-input-group">
              <label className="form-label">Account ID (GUID)</label>
              <div className="search-row">
                <input
                  type="text"
                  className="input"
                  placeholder="e.g., 550e8400-e29b-41d4-a716-446655440000"
                  value={searchId}
                  onChange={(e) => setSearchId(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                />
                <button className="btn btn-primary" onClick={handleSearch}>
                  🔍 Search
                </button>
              </div>
            </div>
          </div>

          {/* Account Details */}
          {selectedAccountId && accountDetail && (
            <div className="player-details">
              <div className="player-card">
                <h2>Account Details</h2>
                <div className="detail-grid">
                  <div className="detail-item">
                    <span className="detail-label">Account ID</span>
                    <code>{accountDetail.accountId}</code>
                  </div>
                  <div className="detail-item">
                    <span className="detail-label">Created</span>
                    <span>{formatDateTime(accountDetail.createdAt)}</span>
                  </div>
                  <div className="detail-item">
                    <span className="detail-label">Cosmetics Unlocked</span>
                    <span>{accountDetail.unlockedCosmetics?.length || 0}</span>
                  </div>
                  <div className="detail-item">
                    <span className="detail-label">Achievements</span>
                    <span>{accountDetail.unlockedAchievements?.length || 0}</span>
                  </div>
                </div>
              </div>

              {/* Characters */}
              <div className="player-card">
                <h2>Characters ({characters?.length || 0})</h2>
                {!characters || characters.length === 0 ? (
                  <p className="text-muted">No characters found.</p>
                ) : (
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Name</th>
                        <th>Season</th>
                        <th>Level</th>
                        <th>Restrictions</th>
                        <th>Status</th>
                        <th>Created</th>
                      </tr>
                    </thead>
                    <tbody>
                      {characters.map((char) => (
                        <tr key={char.characterId} className={char.isDead ? 'row-dead' : ''}>
                          <td>{char.name}</td>
                          <td><code>{char.seasonId}</code></td>
                          <td>{char.level}</td>
                          <td>
                            {getRestrictionBadges(char.restrictions).length > 0 ? (
                              getRestrictionBadges(char.restrictions).map((badge) => (
                                <span key={badge} className="badge badge-restriction">{badge}</span>
                              ))
                            ) : (
                              <span className="text-muted">None</span>
                            )}
                          </td>
                          <td>
                            {char.isDead ? (
                              <span className="badge badge-dead">☠️ Dead</span>
                            ) : (
                              <span className="badge badge-alive">✅ Alive</span>
                            )}
                          </td>
                          <td>{formatDate(char.createdAt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>

              {/* Cosmetics */}
              {accountDetail.unlockedCosmetics && accountDetail.unlockedCosmetics.length > 0 && (
                <div className="player-card">
                  <h2>Unlocked Cosmetics</h2>
                  <div className="tag-list">
                    {accountDetail.unlockedCosmetics.map((cosmetic) => (
                      <span key={cosmetic} className="tag">{cosmetic}</span>
                    ))}
                  </div>
                </div>
              )}

              {/* Achievements */}
              {accountDetail.unlockedAchievements && accountDetail.unlockedAchievements.length > 0 && (
                <div className="player-card">
                  <h2>Achievements</h2>
                  <div className="tag-list">
                    {accountDetail.unlockedAchievements.map((achievement) => (
                      <span key={achievement} className="tag tag-achievement">🏆 {achievement}</span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}

          {hasSearched && !accountDetail && selectedAccountId && (
            <div className="empty-state">
              <p>No account found with that ID.</p>
            </div>
          )}
        </>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm && deleteTarget && (
        <div className="modal-overlay" onClick={() => setShowDeleteConfirm(false)}>
          <div className="modal modal-small" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">⚠️ Delete Account</h2>
            </div>
            <div className="modal-body">
              <p>Are you sure you want to delete account:</p>
              <p><code>{deleteTarget.accountId}</code></p>
              <p className="text-muted">This will permanently remove the account data. This action cannot be undone.</p>
            </div>
            <div className="modal-footer">
              <button 
                type="button" 
                className="btn btn-secondary" 
                onClick={() => setShowDeleteConfirm(false)}
              >
                Cancel
              </button>
              <button 
                type="button" 
                className="btn btn-danger"
                onClick={() => deleteMutation.mutate(deleteTarget.accountId)}
                disabled={deleteMutation.isPending}
              >
                {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
