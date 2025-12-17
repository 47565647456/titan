import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { adminUsersApi } from '../api/client';
import { useAuth } from '../contexts/AuthContext';
import type { AdminUser, UpdateAdminUserRequest } from '../types';
import './DataPage.css';

export function AdminUsersPage() {
  const queryClient = useQueryClient();
  const { user: currentUser } = useAuth();
  const [showModal, setShowModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<AdminUser | null>(null);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [formData, setFormData] = useState({
    email: '',
    password: '',
    displayName: '',
    roles: [] as string[],
  });

  const { data: users, isLoading } = useQuery({
    queryKey: ['adminUsers'],
    queryFn: adminUsersApi.getAll,
  });

  const { data: availableRoles } = useQuery({
    queryKey: ['adminRoles'],
    queryFn: adminUsersApi.getRoles,
  });

  const createMutation = useMutation({
    mutationFn: adminUsersApi.create,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
      closeModal();
    },
    onError: (err: Error) => setError(err.message),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateAdminUserRequest }) =>
      adminUsersApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
      closeModal();
    },
    onError: (err: Error) => setError(err.message),
  });

  const deleteMutation = useMutation({
    mutationFn: adminUsersApi.delete,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['adminUsers'] });
      setShowDeleteConfirm(false);
      setDeleteTarget(null);
    },
    onError: (err: Error) => setError(err.message),
  });

  const closeModal = () => {
    setShowModal(false);
    setEditingUser(null);
    setError(null);
    setFormData({ email: '', password: '', displayName: '', roles: [] });
  };

  const openEdit = (user: AdminUser) => {
    setEditingUser(user);
    setFormData({
      email: user.email,
      password: '',
      displayName: user.displayName || '',
      roles: user.roles,
    });
    setError(null);
    setShowModal(true);
  };

  const openDeleteConfirm = (user: AdminUser) => {
    setDeleteTarget(user);
    setShowDeleteConfirm(true);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    
    if (editingUser) {
      updateMutation.mutate({
        id: editingUser.id,
        data: { displayName: formData.displayName, roles: formData.roles },
      });
    } else {
      if (!formData.password) {
        setError('Password is required');
        return;
      }
      createMutation.mutate({
        email: formData.email,
        password: formData.password,
        displayName: formData.displayName || undefined,
        roles: formData.roles,
      });
    }
  };

  const toggleRole = (role: string) => {
    setFormData((prev) => ({
      ...prev,
      roles: prev.roles.includes(role)
        ? prev.roles.filter((r) => r !== role)
        : [...prev.roles, role],
    }));
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

  const getRoleBadgeClass = (role: string): string => {
    switch (role.toLowerCase()) {
      case 'superadmin':
        return 'badge-superadmin';
      case 'admin':
        return 'badge-admin';
      case 'viewer':
        return 'badge-viewer';
      default:
        return 'badge-primary';
    }
  };

  return (
    <div className="data-page">
      <div className="page-header">
        <div>
          <h1>👤 Admin Users</h1>
          <p className="subtitle">Manage dashboard administrator accounts</p>
        </div>
        <button className="btn btn-primary" onClick={() => setShowModal(true)}>
          ➕ Create Admin User
        </button>
      </div>

      {isLoading ? (
        <div className="loading-state">
          <span className="spinner" />
          <span>Loading admin users...</span>
        </div>
      ) : !users || users.length === 0 ? (
        <div className="empty-state">
          <p>No admin users found.</p>
        </div>
      ) : (
        <div className="card">
          <div className="card-body" style={{ padding: 0 }}>
            <table className="table">
              <thead>
                <tr>
                  <th>Email</th>
                  <th>Display Name</th>
                  <th>Roles</th>
                  <th>Created</th>
                  <th>Last Login</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <tr key={user.id}>
                    <td>{user.email}</td>
                    <td>{user.displayName || '—'}</td>
                    <td>
                      <div className="role-badges">
                        {user.roles.map((role) => (
                          <span key={role} className={`badge ${getRoleBadgeClass(role)}`}>
                            {role}
                          </span>
                        ))}
                      </div>
                    </td>
                    <td>{formatDate(user.createdAt)}</td>
                    <td>{user.lastLoginAt ? formatDateTime(user.lastLoginAt) : 'Never'}</td>
                    <td>
                      <div className="action-buttons">
                        <button
                          className="btn btn-sm btn-secondary"
                          onClick={() => openEdit(user)}
                        >
                          Edit
                        </button>
                        {user.email !== currentUser?.email && (
                          <button
                            className="btn btn-sm btn-danger"
                            onClick={() => openDeleteConfirm(user)}
                          >
                            Delete
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

      {/* Create/Edit Modal */}
      {showModal && (
        <div className="modal-overlay" onClick={closeModal}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">
                {editingUser ? 'Edit Admin User' : 'Create Admin User'}
              </h2>
              <button className="btn btn-ghost btn-sm" onClick={closeModal}>✕</button>
            </div>
            <form onSubmit={handleSubmit}>
              <div className="modal-body">
                <div className="form-group">
                  <label className="form-label">Email</label>
                  <input
                    type="email"
                    className="input"
                    value={formData.email}
                    onChange={(e) => setFormData({ ...formData, email: e.target.value })}
                    required
                    disabled={!!editingUser}
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Display Name</label>
                  <input
                    type="text"
                    className="input"
                    value={formData.displayName}
                    onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
                  />
                </div>
                {!editingUser && (
                  <div className="form-group">
                    <label className="form-label">Password</label>
                    <input
                      type="password"
                      className="input"
                      value={formData.password}
                      onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                      required
                    />
                  </div>
                )}
                <div className="form-group">
                  <label className="form-label">Roles</label>
                  <div className="role-checkboxes">
                    {availableRoles?.map((role) => (
                      <label key={role} className="role-checkbox">
                        <input
                          type="checkbox"
                          checked={formData.roles.includes(role)}
                          onChange={() => toggleRole(role)}
                        />
                        <span>{role}</span>
                      </label>
                    ))}
                  </div>
                </div>

                {error && <div className="alert alert-danger">{error}</div>}
              </div>
              <div className="modal-footer">
                <button type="button" className="btn btn-secondary" onClick={closeModal}>
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  disabled={createMutation.isPending || updateMutation.isPending}
                >
                  {createMutation.isPending || updateMutation.isPending ? 'Saving...' : 'Save'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm && deleteTarget && (
        <div className="modal-overlay" onClick={() => setShowDeleteConfirm(false)}>
          <div className="modal modal-small" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">⚠️ Delete Admin User</h2>
            </div>
            <div className="modal-body">
              <p>Are you sure you want to delete <strong>{deleteTarget.email}</strong>?</p>
              <p className="text-muted">This action cannot be undone.</p>
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
                onClick={() => deleteMutation.mutate(deleteTarget.id)}
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
