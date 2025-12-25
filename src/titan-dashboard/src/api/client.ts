import axios from 'axios';
import type { 
  LoginRequest, 
  LoginResponse, 
  AdminUser,
  CreateAdminUserRequest,
  UpdateAdminUserRequest,
  AccountSummary,
  AccountDetail,
  CharacterSummary,
  Season,
  CreateSeasonRequest,
  SeasonStatus,
  BaseType,
  CreateBaseTypeRequest,
  RateLimitingConfiguration,
  RateLimitPolicy,
  EndpointRateLimitConfig,
  RateLimitMetrics,
  SessionInfo,
  SessionListResponse,
  ServerMessage,
  SendBroadcastRequest
} from '../types';

// Create axios instance with base configuration
const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '/api',
  headers: {
    'Content-Type': 'application/json',
  },
  withCredentials: true, // Send httpOnly cookies with requests
});

// Request interceptor to add auth token from localStorage
api.interceptors.request.use((config) => {
  const sessionId = localStorage.getItem('sessionId');
  if (sessionId) {
    config.headers.Authorization = `Bearer ${sessionId}`;
  }
  return config;
});

// Response interceptor for handling 401s - redirect to login (no refresh with session auth)
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    // If 401, session is invalid/expired - redirect to login
    if (error.response?.status === 401) {
      localStorage.removeItem('sessionId');
      localStorage.removeItem('user');
      window.location.href = '/login';
    }
    
    return Promise.reject(error);
  }
);

// Auth API
export const authApi = {
  login: async (data: LoginRequest): Promise<LoginResponse> => {
    const response = await api.post<LoginResponse>('/admin/auth/login', data);
    return response.data;
  },
  
  logout: async (): Promise<void> => {
    await api.post('/admin/auth/logout');
  },
  
  revokeAll: async (): Promise<void> => {
    await api.post('/admin/auth/revoke-all');
  },
  
  getCurrentUser: async (): Promise<AdminUser> => {
    const response = await api.get<AdminUser>('/admin/auth/me');
    return response.data;
  },
};

// Admin Users API
export const adminUsersApi = {
  getAll: async (): Promise<AdminUser[]> => {
    const response = await api.get<AdminUser[]>('/admin/users');
    return response.data;
  },
  
  getById: async (id: string): Promise<AdminUser> => {
    const response = await api.get<AdminUser>(`/admin/users/${id}`);
    return response.data;
  },
  
  create: async (data: CreateAdminUserRequest): Promise<AdminUser> => {
    const response = await api.post<AdminUser>('/admin/users', data);
    return response.data;
  },
  
  update: async (id: string, data: UpdateAdminUserRequest): Promise<AdminUser> => {
    const response = await api.put<AdminUser>(`/admin/users/${id}`, data);
    return response.data;
  },
  
  delete: async (id: string): Promise<void> => {
    await api.delete(`/admin/users/${id}`);
  },
  
  getRoles: async (): Promise<string[]> => {
    const response = await api.get<string[]>('/admin/users/roles');
    return response.data;
  },
};

// Accounts API
export const accountsApi = {
  getAll: async (): Promise<AccountSummary[]> => {
    const response = await api.get<AccountSummary[]>('/admin/accounts');
    return response.data;
  },
  
  getById: async (id: string): Promise<AccountDetail> => {
    const response = await api.get<AccountDetail>(`/admin/accounts/${id}`);
    return response.data;
  },
  
  getCharacters: async (id: string): Promise<CharacterSummary[]> => {
    const response = await api.get<CharacterSummary[]>(`/admin/accounts/${id}/characters`);
    return response.data;
  },
  
  create: async (): Promise<AccountDetail> => {
    const response = await api.post<AccountDetail>('/admin/accounts');
    return response.data;
  },
  
  delete: async (id: string): Promise<void> => {
    await api.delete(`/admin/accounts/${id}`);
  },
};

// Seasons API
export const seasonsApi = {
  getAll: async (): Promise<Season[]> => {
    const response = await api.get<Season[]>('/admin/seasons');
    return response.data;
  },
  
  getById: async (id: string): Promise<Season> => {
    const response = await api.get<Season>(`/admin/seasons/${id}`);
    return response.data;
  },
  
  create: async (data: CreateSeasonRequest): Promise<Season> => {
    const response = await api.post<Season>('/admin/seasons', data);
    return response.data;
  },
  
  updateStatus: async (id: string, status: SeasonStatus): Promise<void> => {
    await api.put(`/admin/seasons/${id}/status`, { status });
  },
  
  end: async (id: string): Promise<void> => {
    await api.post(`/admin/seasons/${id}/end`);
  },
};

// Base Types API
export const baseTypesApi = {
  getAll: async (): Promise<BaseType[]> => {
    const response = await api.get<BaseType[]>('/admin/base-types');
    return response.data;
  },
  
  getById: async (id: string): Promise<BaseType> => {
    const response = await api.get<BaseType>(`/admin/base-types/${id}`);
    return response.data;
  },
  
  create: async (data: CreateBaseTypeRequest): Promise<BaseType> => {
    const response = await api.post<BaseType>('/admin/base-types', data);
    return response.data;
  },
  
  update: async (id: string, data: Partial<BaseType>): Promise<BaseType> => {
    const response = await api.put<BaseType>(`/admin/base-types/${id}`, data);
    return response.data;
  },
  
  delete: async (id: string): Promise<void> => {
    await api.delete(`/admin/base-types/${id}`);
  },
};

// Rate Limiting API
export const rateLimitingApi = {
  getConfig: async (): Promise<RateLimitingConfiguration> => {
    const response = await api.get<RateLimitingConfiguration>('/admin/rate-limiting/config');
    return response.data;
  },
  
  setEnabled: async (enabled: boolean): Promise<void> => {
    await api.post('/admin/rate-limiting/enabled', { enabled });
  },
  
  upsertPolicy: async (name: string, rules: string[]): Promise<RateLimitPolicy> => {
    const response = await api.post<RateLimitPolicy>('/admin/rate-limiting/policies', { name, rules });
    return response.data;
  },
  
  deletePolicy: async (name: string): Promise<void> => {
    await api.delete(`/admin/rate-limiting/policies/${encodeURIComponent(name)}`);
  },
  
  setDefaultPolicy: async (policyName: string): Promise<void> => {
    await api.post('/admin/rate-limiting/default-policy', { policyName });
  },
  
  addEndpointMapping: async (pattern: string, policyName: string): Promise<EndpointRateLimitConfig> => {
    const response = await api.post<EndpointRateLimitConfig>('/admin/rate-limiting/mappings', { pattern, policyName });
    return response.data;
  },
  
  removeEndpointMapping: async (pattern: string): Promise<void> => {
    await api.delete(`/admin/rate-limiting/mappings/${encodeURIComponent(pattern)}`);
  },
  
  reset: async (): Promise<void> => {
    await api.post('/admin/rate-limiting/reset');
  },
  
  getMetrics: async (): Promise<RateLimitMetrics> => {
    const response = await api.get<RateLimitMetrics>('/admin/rate-limiting/metrics');
    return response.data;
  },

  getMetricsHistory: async (count = 60): Promise<MetricsHistoryItem[]> => {
    const response = await api.get<MetricsHistoryItem[]>(`/admin/rate-limiting/metrics/history?count=${count}`);
    return response.data;
  },

  getMetricsCollectionStatus: async (): Promise<MetricsCollectionStatus> => {
    const response = await api.get<MetricsCollectionStatus>('/admin/rate-limiting/metrics/collection');
    return response.data;
  },

  setMetricsCollection: async (enabled: boolean): Promise<void> => {
    await api.post('/admin/rate-limiting/metrics/collection', { enabled });
  },

  clearMetricsHistory: async (): Promise<void> => {
    await api.delete('/admin/rate-limiting/metrics/history');
  },
};

// Sessions API
export const sessionsApi = {
  getAll: async (skip = 0, take = 50): Promise<SessionListResponse> => {
    const response = await api.get<SessionListResponse>(`/admin/sessions?skip=${skip}&take=${take}`);
    return response.data;
  },

  getCount: async (): Promise<{ count: number }> => {
    const response = await api.get<{ count: number }>('/admin/sessions/count');
    return response.data;
  },

  getByUserId: async (userId: string): Promise<SessionInfo[]> => {
    const response = await api.get<SessionInfo[]>(`/admin/sessions/user/${userId}`);
    return response.data;
  },

  invalidate: async (ticketId: string): Promise<{ success: boolean }> => {
    const response = await api.delete<{ success: boolean }>(`/admin/sessions/${encodeURIComponent(ticketId)}`);
    return response.data;
  },

  invalidateAllForUser: async (userId: string): Promise<{ count: number }> => {
    const response = await api.delete<{ count: number }>(`/admin/sessions/user/${userId}`);
    return response.data;
  },
};

// Broadcast API
export const broadcastApi = {
  send: async (request: SendBroadcastRequest): Promise<ServerMessage> => {
    const response = await api.post<ServerMessage>('/admin/broadcast', request);
    return response.data;
  },
};

export interface MetricsHistoryItem {
  timestamp: string;
  activeBuckets: number;
  activeTimeouts: number;
  totalRequests: number;
}

export interface MetricsCollectionStatus {
  enabled: boolean;
}

// System Health API (authenticated - requires admin login)
export interface HealthCheckResult {
  name: string;
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  duration: number;
  description?: string;
  exception?: string;
}

export interface HealthCheckResponse {
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  totalDuration: number;
  checks: HealthCheckResult[];
}

export const systemApi = {
  getHealth: async (): Promise<HealthCheckResponse> => {
    const response = await api.get<HealthCheckResponse>('/admin/system/health');
    return response.data;
  },
};

// Backwards compatibility alias
export const healthApi = {
  getStatus: async (): Promise<HealthCheckResponse> => {
    return systemApi.getHealth();
  },
};

export default api;
