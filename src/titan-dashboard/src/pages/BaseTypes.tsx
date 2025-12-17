import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { baseTypesApi } from '../api/client';
import { ItemCategory, EquipmentSlot, type BaseType, type CreateBaseTypeRequest } from '../types';
import './DataPage.css';

export function BaseTypesPage() {
  const queryClient = useQueryClient();
  const [showModal, setShowModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<BaseType | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formData, setFormData] = useState<CreateBaseTypeRequest>({
    baseTypeId: '',
    name: '',
    description: '',
    category: ItemCategory.Equipment,
    slot: EquipmentSlot.None,
    width: 1,
    height: 1,
    maxStackSize: 1,
    isTradeable: true,
  });

  const { data: baseTypes, isLoading } = useQuery({
    queryKey: ['baseTypes'],
    queryFn: baseTypesApi.getAll,
  });

  const createMutation = useMutation({
    mutationFn: baseTypesApi.create,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['baseTypes'] });
      closeModal();
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: Partial<BaseType> }) =>
      baseTypesApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['baseTypes'] });
      closeModal();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: baseTypesApi.delete,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['baseTypes'] });
      setShowDeleteConfirm(false);
      setDeleteTarget(null);
    },
  });

  const closeModal = () => {
    setShowModal(false);
    setEditingId(null);
    setFormData({
      baseTypeId: '',
      name: '',
      description: '',
      category: ItemCategory.Equipment,
      slot: EquipmentSlot.None,
      width: 1,
      height: 1,
      maxStackSize: 1,
      isTradeable: true,
    });
  };

  const openEdit = (item: BaseType) => {
    setEditingId(item.baseTypeId);
    setFormData({
      baseTypeId: item.baseTypeId,
      name: item.name,
      description: item.description || '',
      category: item.category,
      slot: item.slot,
      width: item.width,
      height: item.height,
      maxStackSize: item.maxStackSize,
      isTradeable: item.isTradeable,
    });
    setShowModal(true);
  };

  const openDeleteConfirm = (item: BaseType) => {
    setDeleteTarget(item);
    setShowDeleteConfirm(true);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (editingId) {
      updateMutation.mutate({ id: editingId, data: formData });
    } else {
      createMutation.mutate(formData);
    }
  };

  const getCategoryName = (cat: ItemCategory) => {
    const names: Record<number, string> = {
      [ItemCategory.Currency]: 'Currency',
      [ItemCategory.Equipment]: 'Equipment',
      [ItemCategory.Gem]: 'Gem',
      [ItemCategory.Map]: 'Map',
      [ItemCategory.Consumable]: 'Consumable',
      [ItemCategory.Material]: 'Material',
      [ItemCategory.Quest]: 'Quest',
    };
    return names[cat] || 'Unknown';
  };

  const getSlotName = (slot: EquipmentSlot) => {
    const names: Record<number, string> = {
      [EquipmentSlot.None]: 'None',
      [EquipmentSlot.MainHand]: 'Main Hand',
      [EquipmentSlot.OffHand]: 'Off Hand',
      [EquipmentSlot.Helmet]: 'Helmet',
      [EquipmentSlot.BodyArmour]: 'Body Armour',
      [EquipmentSlot.Gloves]: 'Gloves',
      [EquipmentSlot.Boots]: 'Boots',
      [EquipmentSlot.Belt]: 'Belt',
      [EquipmentSlot.Amulet]: 'Amulet',
      [EquipmentSlot.RingLeft]: 'Ring (Left)',
      [EquipmentSlot.RingRight]: 'Ring (Right)',
    };
    return names[slot] || 'Unknown';
  };

  return (
    <div className="data-page">
      <div className="page-header">
        <div>
          <h1>📦 Base Types</h1>
          <p className="subtitle">Manage item base type definitions</p>
        </div>
        <button className="btn btn-primary" onClick={() => setShowModal(true)}>
          ➕ Create Base Type
        </button>
      </div>

      {isLoading ? (
        <div className="loading-state">
          <span className="spinner" />
          <span>Loading base types...</span>
        </div>
      ) : !baseTypes || baseTypes.length === 0 ? (
        <div className="empty-state">
          <p>No base types found.</p>
        </div>
      ) : (
        <div className="card">
          <div className="card-body" style={{ padding: 0 }}>
            <table className="table">
              <thead>
                <tr>
                  <th>ID</th>
                  <th>Name</th>
                  <th>Category</th>
                  <th>Slot</th>
                  <th>Size</th>
                  <th>Stack</th>
                  <th>Tradeable</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {baseTypes.map((item) => (
                  <tr key={item.baseTypeId}>
                    <td><code>{item.baseTypeId}</code></td>
                    <td>{item.name}</td>
                    <td>
                      <span className="badge badge-primary">{getCategoryName(item.category)}</span>
                    </td>
                    <td>{getSlotName(item.slot)}</td>
                    <td>{item.width}×{item.height}</td>
                    <td>{item.maxStackSize}</td>
                    <td>{item.isTradeable ? '✅' : '❌'}</td>
                    <td>
                      <div className="action-buttons">
                        <button
                          className="btn btn-sm btn-secondary"
                          onClick={() => openEdit(item)}
                        >
                          Edit
                        </button>
                        <button
                          className="btn btn-sm btn-danger"
                          onClick={() => openDeleteConfirm(item)}
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
      )}

      {/* Create/Edit Modal */}
      {showModal && (
        <div className="modal-overlay" onClick={closeModal}>
          <div className="modal modal-large" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">
                {editingId ? 'Edit Base Type' : 'Create Base Type'}
              </h2>
              <button className="btn btn-ghost btn-sm" onClick={closeModal}>✕</button>
            </div>
            <form onSubmit={handleSubmit}>
              <div className="modal-body">
                <div className="form-group">
                  <label className="form-label">Base Type ID</label>
                  <input
                    type="text"
                    className="input"
                    value={formData.baseTypeId}
                    onChange={(e) => setFormData({ ...formData, baseTypeId: e.target.value })}
                    required
                    disabled={!!editingId}
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Name</label>
                  <input
                    type="text"
                    className="input"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Description</label>
                  <textarea
                    className="input"
                    rows={3}
                    value={formData.description}
                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                  />
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Category</label>
                    <select
                      className="input"
                      value={formData.category}
                      onChange={(e) => setFormData({ ...formData, category: Number(e.target.value) as ItemCategory })}
                    >
                      <option value={ItemCategory.Currency}>Currency</option>
                      <option value={ItemCategory.Equipment}>Equipment</option>
                      <option value={ItemCategory.Gem}>Gem</option>
                      <option value={ItemCategory.Map}>Map</option>
                      <option value={ItemCategory.Consumable}>Consumable</option>
                      <option value={ItemCategory.Material}>Material</option>
                      <option value={ItemCategory.Quest}>Quest</option>
                    </select>
                  </div>
                  <div className="form-group">
                    <label className="form-label">Equipment Slot</label>
                    <select
                      className="input"
                      value={formData.slot}
                      onChange={(e) => setFormData({ ...formData, slot: Number(e.target.value) as EquipmentSlot })}
                    >
                      <option value={EquipmentSlot.None}>None</option>
                      <option value={EquipmentSlot.MainHand}>Main Hand</option>
                      <option value={EquipmentSlot.OffHand}>Off Hand</option>
                      <option value={EquipmentSlot.Helmet}>Helmet</option>
                      <option value={EquipmentSlot.BodyArmour}>Body Armour</option>
                      <option value={EquipmentSlot.Gloves}>Gloves</option>
                      <option value={EquipmentSlot.Boots}>Boots</option>
                      <option value={EquipmentSlot.Belt}>Belt</option>
                      <option value={EquipmentSlot.Amulet}>Amulet</option>
                      <option value={EquipmentSlot.RingLeft}>Ring (Left)</option>
                      <option value={EquipmentSlot.RingRight}>Ring (Right)</option>
                    </select>
                  </div>
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label className="form-label">Width</label>
                    <input
                      type="number"
                      className="input"
                      min={1}
                      value={formData.width}
                      onChange={(e) => setFormData({ ...formData, width: parseInt(e.target.value) || 1 })}
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Height</label>
                    <input
                      type="number"
                      className="input"
                      min={1}
                      value={formData.height}
                      onChange={(e) => setFormData({ ...formData, height: parseInt(e.target.value) || 1 })}
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Max Stack Size</label>
                    <input
                      type="number"
                      className="input"
                      min={1}
                      value={formData.maxStackSize}
                      onChange={(e) => setFormData({ ...formData, maxStackSize: parseInt(e.target.value) || 1 })}
                    />
                  </div>
                </div>
                <div className="form-group checkbox-group">
                  <label className="checkbox-label">
                    <input
                      type="checkbox"
                      checked={formData.isTradeable}
                      onChange={(e) => setFormData({ ...formData, isTradeable: e.target.checked })}
                    />
                    <span>Tradeable</span>
                  </label>
                </div>
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
                  {editingId ? 'Update' : 'Create'}
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
              <h2 className="modal-title">⚠️ Delete Base Type</h2>
            </div>
            <div className="modal-body">
              <p>Are you sure you want to delete <strong>{deleteTarget.name}</strong>?</p>
              <p><code>{deleteTarget.baseTypeId}</code></p>
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
                onClick={() => deleteMutation.mutate(deleteTarget.baseTypeId)}
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
