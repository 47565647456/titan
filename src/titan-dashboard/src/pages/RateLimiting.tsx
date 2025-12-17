import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { rateLimitingApi } from '../api/client';
import type { RateLimitPolicy } from '../types';
import './DataPage.css';
import './RateLimiting.css';

interface RuleFormData {
  maxHits: number;
  periodSeconds: number;
  timeoutSeconds: number;
}

interface PolicyFormData {
  name: string;
  rules: RuleFormData[];
}

interface MappingFormData {
  pattern: string;
  policyName: string;
}

export function RateLimitingPage() {
  const queryClient = useQueryClient();
  
  // Form state
  const [showPolicyModal, setShowPolicyModal] = useState(false);
  const [showMappingModal, setShowMappingModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [showResetConfirm, setShowResetConfirm] = useState(false);
  const [editingPolicy, setEditingPolicy] = useState<RateLimitPolicy | null>(null);
  const [editingMapping, setEditingMapping] = useState<{ pattern: string; policyName: string } | null>(null);
  const [deletingPolicy, setDeletingPolicy] = useState<RateLimitPolicy | null>(null);
  const [error, setError] = useState<string | null>(null);
  
  const [policyForm, setPolicyForm] = useState<PolicyFormData>({
    name: '',
    rules: [{ maxHits: 100, periodSeconds: 60, timeoutSeconds: 300 }],
  });
  
  const [mappingForm, setMappingForm] = useState<MappingFormData>({
    pattern: '',
    policyName: '',
  });

  const { data: config, isLoading } = useQuery({
    queryKey: ['rateLimitConfig'],
    queryFn: rateLimitingApi.getConfig,
  });

  const toggleMutation = useMutation({
    mutationFn: rateLimitingApi.setEnabled,
    onSuccess: (_data, enabled) => {
      queryClient.setQueryData(['rateLimitConfig'], (oldData: typeof config) => {
        if (!oldData) return oldData;
        return { ...oldData, enabled };
      });
    },
  });

  const upsertPolicyMutation = useMutation({
    mutationFn: ({ name, rules }: { name: string; rules: string[] }) => 
      rateLimitingApi.upsertPolicy(name, rules),
    onSuccess: (result) => {
      queryClient.setQueryData(['rateLimitConfig'], (oldData: typeof config) => {
        if (!oldData) return oldData;
        const existingIndex = oldData.policies.findIndex(p => p.name === result.name);
        if (existingIndex >= 0) {
          const updatedPolicies = [...oldData.policies];
          updatedPolicies[existingIndex] = result;
          return { ...oldData, policies: updatedPolicies };
        } else {
          return { ...oldData, policies: [...oldData.policies, result] };
        }
      });
      closePolicyModal();
    },
    onError: (err: Error) => setError(err.message),
  });

  const deletePolicyMutation = useMutation({
    mutationFn: rateLimitingApi.deletePolicy,
    onSuccess: (_data, deletedName) => {
      queryClient.setQueryData(['rateLimitConfig'], (oldData: typeof config) => {
        if (!oldData) return oldData;
        return { ...oldData, policies: oldData.policies.filter(p => p.name !== deletedName) };
      });
      setShowDeleteConfirm(false);
      setDeletingPolicy(null);
    },
  });

  const setDefaultMutation = useMutation({
    mutationFn: rateLimitingApi.setDefaultPolicy,
    onSuccess: (_data, policyName) => {
      queryClient.setQueryData(['rateLimitConfig'], (oldData: typeof config) => {
        if (!oldData) return oldData;
        return { ...oldData, defaultPolicyName: policyName };
      });
    },
  });

  const addMappingMutation = useMutation({
    mutationFn: ({ pattern, policyName }: MappingFormData) => 
      rateLimitingApi.addEndpointMapping(pattern, policyName),
    onSuccess: (result) => {
      // Directly update the cache to avoid stale data/duplicates
      queryClient.setQueryData(['rateLimitConfig'], (oldData: typeof config) => {
        if (!oldData) return oldData;
        
        // Check if this pattern already exists (edit case)
        const existingIndex = oldData.endpointMappings.findIndex(
          (m: { pattern: string }) => m.pattern === result.pattern
        );
        
        if (existingIndex >= 0) {
          // Update existing mapping
          const updatedMappings = [...oldData.endpointMappings];
          updatedMappings[existingIndex] = result;
          return { ...oldData, endpointMappings: updatedMappings };
        } else {
          // Add new mapping
          return { ...oldData, endpointMappings: [...oldData.endpointMappings, result] };
        }
      });
      closeMappingModal();
    },
    onError: (err: Error) => setError(err.message),
  });

  const removeMappingMutation = useMutation({
    mutationFn: rateLimitingApi.removeEndpointMapping,
    onSuccess: (_data, pattern) => {
      queryClient.setQueryData(['rateLimitConfig'], (oldData: typeof config) => {
        if (!oldData) return oldData;
        return { ...oldData, endpointMappings: oldData.endpointMappings.filter(m => m.pattern !== pattern) };
      });
    },
  });

  const resetMutation = useMutation({
    mutationFn: rateLimitingApi.reset,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['rateLimitConfig'] });
      setShowResetConfirm(false);
    },
  });

  // Modal handlers
  const openCreatePolicyModal = () => {
    setPolicyForm({
      name: '',
      rules: [{ maxHits: 100, periodSeconds: 60, timeoutSeconds: 300 }],
    });
    setEditingPolicy(null);
    setError(null);
    setShowPolicyModal(true);
  };

  const openEditPolicyModal = (policy: RateLimitPolicy) => {
    setPolicyForm({
      name: policy.name,
      rules: policy.rules.map((r) => ({
        maxHits: r.maxHits,
        periodSeconds: r.periodSeconds,
        timeoutSeconds: r.timeoutSeconds,
      })),
    });
    setEditingPolicy(policy);
    setError(null);
    setShowPolicyModal(true);
  };

  const closePolicyModal = () => {
    setShowPolicyModal(false);
    setEditingPolicy(null);
    setError(null);
  };

  const openMappingModal = () => {
    setMappingForm({
      pattern: '',
      policyName: config?.policies[0]?.name || '',
    });
    setEditingMapping(null);
    setError(null);
    setShowMappingModal(true);
  };

  const openEditMappingModal = (mapping: { pattern: string; policyName: string }) => {
    setMappingForm({
      pattern: mapping.pattern,
      policyName: mapping.policyName,
    });
    setEditingMapping(mapping);
    setError(null);
    setShowMappingModal(true);
  };

  const closeMappingModal = () => {
    setShowMappingModal(false);
    setEditingMapping(null);
    setError(null);
  };

  const openDeleteConfirm = (policy: RateLimitPolicy) => {
    setDeletingPolicy(policy);
    setShowDeleteConfirm(true);
  };

  // Form handlers
  const addRule = () => {
    setPolicyForm((prev) => ({
      ...prev,
      rules: [...prev.rules, { maxHits: 100, periodSeconds: 60, timeoutSeconds: 300 }],
    }));
  };

  const removeRule = (index: number) => {
    if (policyForm.rules.length > 1) {
      setPolicyForm((prev) => ({
        ...prev,
        rules: prev.rules.filter((_, i) => i !== index),
      }));
    }
  };

  const updateRule = (index: number, field: keyof RuleFormData, value: number) => {
    setPolicyForm((prev) => ({
      ...prev,
      rules: prev.rules.map((rule, i) => 
        i === index ? { ...rule, [field]: value } : rule
      ),
    }));
  };

  const handleSavePolicy = () => {
    if (!policyForm.name.trim()) {
      setError('Policy name is required');
      return;
    }
    if (policyForm.rules.length === 0) {
      setError('At least one rule is required');
      return;
    }
    
    // Convert rules to string format: "MaxHits:PeriodSeconds:TimeoutSeconds"
    const ruleStrings = policyForm.rules.map(
      (r) => `${r.maxHits}:${r.periodSeconds}:${r.timeoutSeconds}`
    );
    
    upsertPolicyMutation.mutate({ name: policyForm.name, rules: ruleStrings });
  };

  const handleSaveMapping = () => {
    if (!mappingForm.pattern.trim()) {
      setError('Pattern is required');
      return;
    }
    if (!mappingForm.policyName) {
      setError('Policy is required');
      return;
    }
    addMappingMutation.mutate(mappingForm);
  };

  // Helpers
  const formatDuration = (seconds: number): string => {
    if (seconds >= 3600) return `${Math.floor(seconds / 3600)}h`;
    if (seconds >= 60) return `${Math.floor(seconds / 60)}m`;
    return `${seconds}s`;
  };

  if (isLoading) {
    return (
      <div className="data-page">
        <div className="loading-state">
          <span className="spinner" />
        </div>
      </div>
    );
  }

  return (
    <div className="data-page rate-limiting-page">
      <div className="page-header">
        <h1>⚡ Rate Limiting</h1>
        <div className="page-actions">
          <Link to="/rate-limiting/metrics" className="btn btn-primary">
            📊 View Metrics
          </Link>
          <button
            className="btn btn-danger"
            onClick={() => setShowResetConfirm(true)}
            disabled={resetMutation.isPending}
          >
            🔄 Reset to Defaults
          </button>
        </div>
      </div>

      {/* Global Settings Card */}
      <div className="config-card">
        <div className="config-header">
          <h2>Global Settings</h2>
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={config?.enabled ?? false}
              onChange={(e) => toggleMutation.mutate(e.target.checked)}
            />
            <span className="toggle-track">
              <span className="toggle-thumb" />
            </span>
            <span className="toggle-label">
              {config?.enabled ? 'Enabled' : 'Disabled'}
            </span>
          </label>
        </div>
        <p className="config-description">
          {config?.enabled ? (
            <span className="status-ok">✅ Rate limiting is active. Requests will be throttled according to policies.</span>
          ) : (
            <span className="status-warning">⚠️ Rate limiting is disabled. All requests will be allowed without limits.</span>
          )}
        </p>
        <p className="text-muted">Default Policy: <strong>{config?.defaultPolicyName}</strong></p>
      </div>

      {/* Policies Section */}
      <div className="section-header">
        <h2>📋 Policies</h2>
        <button className="btn btn-primary" onClick={openCreatePolicyModal}>
          ➕ Add Policy
        </button>
      </div>

      {config?.policies.length === 0 ? (
        <div className="empty-state">
          <p>No policies configured.</p>
        </div>
      ) : (
        <div className="cards-grid">
          {config?.policies.map((policy) => (
            <div 
              key={policy.name} 
              className={`policy-card ${policy.name === config.defaultPolicyName ? 'default-policy' : ''}`}
            >
              <div className="policy-header">
                <h3>{policy.name}</h3>
                {policy.name === config.defaultPolicyName && (
                  <span className="badge badge-default">Default</span>
                )}
              </div>
              <div className="policy-rules">
                {policy.rules.map((rule, i) => (
                  <div key={i} className="rule-item">
                    <span className="rule-limits">{rule.maxHits} requests</span>
                    <span className="rule-period">per {formatDuration(rule.periodSeconds)}</span>
                    <span className="rule-timeout">{formatDuration(rule.timeoutSeconds)} timeout</span>
                  </div>
                ))}
              </div>
              <div className="policy-actions">
                <button 
                  className="btn btn-sm btn-secondary"
                  onClick={() => openEditPolicyModal(policy)}
                >
                  Edit
                </button>
                {policy.name !== config.defaultPolicyName && (
                  <button 
                    className="btn btn-sm btn-secondary"
                    onClick={() => setDefaultMutation.mutate(policy.name)}
                  >
                    Set Default
                  </button>
                )}
                <button 
                  className="btn btn-sm btn-danger"
                  onClick={() => openDeleteConfirm(policy)}
                  disabled={policy.name === config.defaultPolicyName}
                >
                  Delete
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Endpoint Mappings Section */}
      <div className="section-header">
        <h2>🔗 Endpoint Mappings</h2>
        <button className="btn btn-primary" onClick={openMappingModal}>
          ➕ Add Mapping
        </button>
      </div>

      {config?.endpointMappings.length === 0 ? (
        <div className="empty-state">
          <p>No endpoint mappings configured. All endpoints will use the default policy.</p>
        </div>
      ) : (
        <div className="card">
          <div className="card-body" style={{ padding: 0 }}>
            <table className="table">
              <thead>
                <tr>
                  <th>Pattern</th>
                  <th>Policy</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {config?.endpointMappings.map((mapping) => (
                  <tr key={mapping.pattern}>
                    <td><code>{mapping.pattern}</code></td>
                    <td>{mapping.policyName}</td>
                    <td>
                      <button 
                        className="btn btn-sm btn-secondary"
                        onClick={() => openEditMappingModal(mapping)}
                        style={{ marginRight: '0.5rem' }}
                      >
                        Edit
                      </button>
                      <button 
                        className="btn btn-sm btn-danger"
                        onClick={() => removeMappingMutation.mutate(mapping.pattern)}
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Create/Edit Policy Modal */}
      {showPolicyModal && (
        <div className="modal-overlay" onClick={closePolicyModal}>
          <div className="modal modal-large" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">
                {editingPolicy ? 'Edit Policy' : 'Create Policy'}
              </h2>
              <button className="btn btn-ghost btn-sm" onClick={closePolicyModal}>✕</button>
            </div>
            <div className="modal-body">
              <div className="form-group">
                <label className="form-label">Policy Name</label>
                <input
                  type="text"
                  className="input"
                  value={policyForm.name}
                  onChange={(e) => setPolicyForm((prev) => ({ ...prev, name: e.target.value }))}
                  disabled={!!editingPolicy}
                />
              </div>
              
              <div className="form-group">
                <label className="form-label">Rules</label>
                <p className="text-muted">Format: MaxHits per PeriodSeconds with TimeoutSeconds penalty</p>
                
                {policyForm.rules.map((rule, index) => (
                  <div key={index} className="rule-editor">
                    <input
                      type="number"
                      className="input input-sm"
                      value={rule.maxHits}
                      onChange={(e) => updateRule(index, 'maxHits', parseInt(e.target.value) || 0)}
                      min={1}
                      style={{ width: 80 }}
                    />
                    <span>requests per</span>
                    <input
                      type="number"
                      className="input input-sm"
                      value={rule.periodSeconds}
                      onChange={(e) => updateRule(index, 'periodSeconds', parseInt(e.target.value) || 0)}
                      min={1}
                      style={{ width: 80 }}
                    />
                    <span>seconds, timeout</span>
                    <input
                      type="number"
                      className="input input-sm"
                      value={rule.timeoutSeconds}
                      onChange={(e) => updateRule(index, 'timeoutSeconds', parseInt(e.target.value) || 0)}
                      min={1}
                      style={{ width: 80 }}
                    />
                    <span>seconds</span>
                    <button 
                      type="button" 
                      className="btn btn-sm btn-danger"
                      onClick={() => removeRule(index)}
                      disabled={policyForm.rules.length === 1}
                    >
                      ✕
                    </button>
                  </div>
                ))}
                
                <button type="button" className="btn btn-sm btn-secondary" onClick={addRule}>
                  ➕ Add Rule
                </button>
              </div>
              
              {error && <div className="alert alert-danger">{error}</div>}
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-secondary" onClick={closePolicyModal}>
                Cancel
              </button>
              <button 
                type="button" 
                className="btn btn-primary"
                onClick={handleSavePolicy}
                disabled={upsertPolicyMutation.isPending}
              >
                {upsertPolicyMutation.isPending ? 'Saving...' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Create Mapping Modal */}
      {showMappingModal && (
        <div className="modal-overlay" onClick={closeMappingModal}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">{editingMapping ? 'Edit Endpoint Mapping' : 'Add Endpoint Mapping'}</h2>
              <button className="btn btn-ghost btn-sm" onClick={closeMappingModal}>✕</button>
            </div>
            <div className="modal-body">
              <div className="form-group">
                <label className="form-label">Pattern</label>
                <input
                  type="text"
                  className="input"
                  placeholder="/api/auth/* or TradeHub.*"
                  value={mappingForm.pattern}
                  onChange={(e) => setMappingForm((prev) => ({ ...prev, pattern: e.target.value }))}
                  disabled={!!editingMapping}
                />
                <p className="text-muted">
                  {editingMapping 
                    ? 'Pattern cannot be changed. To change, delete and recreate.'
                    : 'Use * as wildcard. HTTP: /api/path/*, SignalR: HubName.*'}
                </p>
              </div>
              
              <div className="form-group">
                <label className="form-label">Policy</label>
                <select
                  className="input"
                  value={mappingForm.policyName}
                  onChange={(e) => setMappingForm((prev) => ({ ...prev, policyName: e.target.value }))}
                >
                  {config?.policies.map((policy) => (
                    <option key={policy.name} value={policy.name}>{policy.name}</option>
                  ))}
                </select>
              </div>
              
              {error && <div className="alert alert-danger">{error}</div>}
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-secondary" onClick={closeMappingModal}>
                Cancel
              </button>
              <button 
                type="button" 
                className="btn btn-primary"
                onClick={handleSaveMapping}
                disabled={addMappingMutation.isPending}
              >
                {addMappingMutation.isPending ? 'Saving...' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Policy Confirmation */}
      {showDeleteConfirm && deletingPolicy && (
        <div className="modal-overlay" onClick={() => setShowDeleteConfirm(false)}>
          <div className="modal modal-small" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">⚠️ Delete Policy</h2>
            </div>
            <div className="modal-body">
              <p>Are you sure you want to delete <strong>{deletingPolicy.name}</strong>?</p>
              <p className="text-muted">Endpoints using this policy will fall back to the default.</p>
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-secondary" onClick={() => setShowDeleteConfirm(false)}>
                Cancel
              </button>
              <button 
                type="button" 
                className="btn btn-danger"
                onClick={() => deletePolicyMutation.mutate(deletingPolicy.name)}
                disabled={deletePolicyMutation.isPending}
              >
                {deletePolicyMutation.isPending ? 'Deleting...' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Reset Confirmation */}
      {showResetConfirm && (
        <div className="modal-overlay" onClick={() => setShowResetConfirm(false)}>
          <div className="modal modal-small" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2 className="modal-title">🔄 Reset to Defaults</h2>
            </div>
            <div className="modal-body">
              <p>This will remove all custom policies and mappings.</p>
              <p className="text-muted">The configuration will be reset to the values from appsettings.json.</p>
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-secondary" onClick={() => setShowResetConfirm(false)}>
                Cancel
              </button>
              <button 
                type="button" 
                className="btn btn-danger"
                onClick={() => resetMutation.mutate()}
                disabled={resetMutation.isPending}
              >
                {resetMutation.isPending ? 'Resetting...' : 'Reset'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
