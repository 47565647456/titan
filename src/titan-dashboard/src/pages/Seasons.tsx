import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { seasonsApi } from '../api/client';
import { SeasonStatus, SeasonType, type Season, type CreateSeasonRequest } from '../types';
import './DataPage.css';

export function SeasonsPage() {
  const queryClient = useQueryClient();
  const [showModal, setShowModal] = useState(false);
  const [showEndConfirm, setShowEndConfirm] = useState(false);
  const [endTarget, setEndTarget] = useState<Season | null>(null);
  const [formData, setFormData] = useState<CreateSeasonRequest>({
    seasonId: '',
    name: '',
    type: SeasonType.Temporary,
    status: SeasonStatus.Upcoming,
    startDate: new Date().toISOString().split('T')[0],
    endDate: '',
    migrationTargetId: 'standard',
    isVoid: false,
  });

  const { data: seasons, isLoading } = useQuery({
    queryKey: ['seasons'],
    queryFn: seasonsApi.getAll,
  });

  const createMutation = useMutation({
    mutationFn: seasonsApi.create,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['seasons'] });
      setShowModal(false);
      resetForm();
    },
  });

  const updateStatusMutation = useMutation({
    mutationFn: ({ id, status }: { id: string; status: SeasonStatus }) =>
      seasonsApi.updateStatus(id, status),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['seasons'] }),
  });

  const endMutation = useMutation({
    mutationFn: seasonsApi.end,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['seasons'] });
      setShowEndConfirm(false);
      setEndTarget(null);
    },
  });

  const resetForm = () => {
    setFormData({
      seasonId: '',
      name: '',
      type: SeasonType.Temporary,
      status: SeasonStatus.Upcoming,
      startDate: new Date().toISOString().split('T')[0],
      endDate: '',
      migrationTargetId: 'standard',
      isVoid: false,
    });
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const data: CreateSeasonRequest = {
      ...formData,
      startDate: new Date(formData.startDate).toISOString(),
      endDate: formData.endDate ? new Date(formData.endDate).toISOString() : undefined,
    };
    createMutation.mutate(data);
  };

  const openEndConfirm = (season: Season) => {
    setEndTarget(season);
    setShowEndConfirm(true);
  };

  const getStatusBadge = (status: SeasonStatus) => {
    switch (status) {
      case SeasonStatus.Upcoming:
        return <span className="badge badge-primary">Upcoming</span>;
      case SeasonStatus.Active:
        return <span className="badge badge-success">Active</span>;
      case SeasonStatus.Ended:
        return <span className="badge badge-warning">Ended</span>;
    }
  };

  const getTypeBadge = (type: SeasonType) => {
    if (type === SeasonType.Permanent) {
      return <span className="badge badge-permanent">Permanent</span>;
    }
    return <span className="badge badge-temporary">Temporary</span>;
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  };

  return (
    <div className="data-page">
      <div className="page-header">
        <div>
          <h1>🗓️ Seasons</h1>
          <p className="subtitle">Manage leagues and seasonal content</p>
        </div>
        <button className="btn btn-primary" onClick={() => setShowModal(true)}>
          ➕ Create Season
        </button>
      </div>

      {isLoading ? (
        <div className="loading-state">
          <span className="spinner" />
          <span>Loading seasons...</span>
        </div>
      ) : !seasons || seasons.length === 0 ? (
        <div className="empty-state">
          <p>No seasons found.</p>
        </div>
      ) : (
        <div className="card">
          <div className="card-body" style={{ padding: 0 }}>
            <table className="table">
              <thead>
                <tr>
                  <th>ID</th>
                  <th>Name</th>
                  <th>Type</th>
                  <th>Status</th>
                  <th>Start</th>
                  <th>End</th>
                  <th>Void</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {seasons.map((season) => (
                  <tr key={season.seasonId}>
                    <td><code>{season.seasonId}</code></td>
                    <td>{season.name}</td>
                    <td>{getTypeBadge(season.type)}</td>
                    <td>{getStatusBadge(season.status)}</td>
                    <td>{formatDate(season.startDate)}</td>
                    <td>{season.endDate ? formatDate(season.endDate) : '—'}</td>
                    <td>{season.isVoid ? '⚠️' : '—'}</td>
                    <td>
                      <div className="action-buttons">
                        {season.status === SeasonStatus.Upcoming && (
                          <button
                            className="btn btn-sm btn-secondary"
                            onClick={() =>
                              updateStatusMutation.mutate({
                                id: season.seasonId,
                                status: SeasonStatus.Active,
                              })
                            }
                          >
                            Start
                          </button>
                        )}
                        {season.status === SeasonStatus.Active && season.type === SeasonType.Temporary && (
                          <button
                            className="btn btn-sm btn-danger"
                            onClick={() => openEndConfirm(season)}
                          >
                            End
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Create Season Modal */}
      {showModal && (
        <div className="modal-overlay" onClick={() => setShowModal(false)}>
          <div className="modal modal-large" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">Create Season</h2>
              <button className="btn btn-ghost btn-sm" onClick={() => setShowModal(false)}>
                ✕
              </button>
            </div>
            <form onSubmit={handleSubmit}>
              <div className="modal-body">
                <div className="form-group">
                  <label className="form-label">Season ID</label>
                  <input
                    type="text"
                    className="input"
                    placeholder="e.g., s1, league-2024"
                    value={formData.seasonId}
                    onChange={(e) => setFormData({ ...formData, seasonId: e.target.value })}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Name</label>
                  <input
                    type="text"
                    className="input"
                    placeholder="e.g., Season 1: The Awakening"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    required
                  />
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Type</label>
                    <select
                      className="input"
                      value={formData.type}
                      onChange={(e) =>
                        setFormData({ ...formData, type: Number(e.target.value) as SeasonType })
                      }
                    >
                      <option value={SeasonType.Temporary}>Temporary</option>
                      <option value={SeasonType.Permanent}>Permanent</option>
                    </select>
                  </div>
                  <div className="form-group">
                    <label className="form-label">Status</label>
                    <select
                      className="input"
                      value={formData.status}
                      onChange={(e) =>
                        setFormData({ ...formData, status: Number(e.target.value) as SeasonStatus })
                      }
                    >
                      <option value={SeasonStatus.Upcoming}>Upcoming</option>
                      <option value={SeasonStatus.Active}>Active</option>
                    </select>
                  </div>
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Start Date</label>
                    <input
                      type="date"
                      className="input"
                      value={formData.startDate}
                      onChange={(e) => setFormData({ ...formData, startDate: e.target.value })}
                      required
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">End Date</label>
                    <input
                      type="date"
                      className="input"
                      value={formData.endDate}
                      onChange={(e) => setFormData({ ...formData, endDate: e.target.value })}
                    />
                  </div>
                </div>
                <div className="form-group">
                  <label className="form-label">Migration Target</label>
                  <input
                    type="text"
                    className="input"
                    placeholder="standard"
                    value={formData.migrationTargetId}
                    onChange={(e) => setFormData({ ...formData, migrationTargetId: e.target.value })}
                  />
                  <p className="text-muted">Season ID where characters migrate after this season ends</p>
                </div>
                <div className="form-group checkbox-group">
                  <label className="checkbox-label">
                    <input
                      type="checkbox"
                      checked={formData.isVoid}
                      onChange={(e) => setFormData({ ...formData, isVoid: e.target.checked })}
                    />
                    <span>Void Season (characters cannot migrate)</span>
                  </label>
                </div>
              </div>
              <div className="modal-footer">
                <button type="button" className="btn btn-secondary" onClick={() => setShowModal(false)}>
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={createMutation.isPending}
                >
                  {createMutation.isPending ? 'Creating...' : 'Create'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* End Season Confirmation Modal */}
      {showEndConfirm && endTarget && (
        <div className="modal-overlay" onClick={() => setShowEndConfirm(false)}>
          <div className="modal modal-small" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">⚠️ End Season</h2>
            </div>
            <div className="modal-body">
              <p>Are you sure you want to end <strong>{endTarget.name}</strong>?</p>
              <p className="text-muted">
                Characters will be migrated to: <code>{endTarget.migrationTargetId || 'standard'}</code>
              </p>
              {endTarget.isVoid && (
                <p className="text-warning">⚠️ This is a void season - characters will NOT migrate.</p>
              )}
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-secondary" onClick={() => setShowEndConfirm(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-danger"
                onClick={() => endMutation.mutate(endTarget.seasonId)}
                disabled={endMutation.isPending}
              >
                {endMutation.isPending ? 'Ending...' : 'End Season'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
