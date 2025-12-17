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
  RateLimitMetrics
} from '../types';

// Response type for token refresh
export interface RefreshResponse {
  success: boolean;
  accessToken: string;
  refreshToken: string;
  expiresInSeconds: number;
}

// Create axios instance with base configuration
const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '/api',
  headers: {
    'Content-Type': 'application/json',
  },
  withCredentials: true, // Send httpOnly cookies with requests
});

// Request interceptor to add auth token from localStorage (fallback for backward compatibility)
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Track if we're currently refreshing to prevent multiple simultaneous refresh attempts
let isRefreshing = false;
let refreshSubscribers: ((error: Error | null) => void)[] = [];

const subscribeTokenRefresh = (cb: (error: Error | null) => void) => {
  refreshSubscribers.push(cb);
};

const onRefreshComplete = (error: Error | null) => {
  refreshSubscribers.forEach(cb => cb(error));
  refreshSubscribers = [];
};

// Response interceptor for handling 401s with automatic refresh
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    // If 401 and not already retrying
    if (error.response?.status === 401 && !originalRequest._retry) {
      // If we're already refreshing, wait for it to complete
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          subscribeTokenRefresh((refreshError) => {
            if (refreshError) {
              reject(refreshError);
            } else {
              // Retry the original request
              resolve(api(originalRequest));
            }
          });
        });
      }

      originalRequest._retry = true;
      isRefreshing = true;

      try {
        // Attempt to refresh the token
        const refreshResponse = await api.post<RefreshResponse>('/admin/auth/refresh');
        
        if (refreshResponse.data.success) {
          // Update localStorage with new tokens (for backward compatibility)
          localStorage.setItem('accessToken', refreshResponse.data.accessToken);
          localStorage.setItem('refreshToken', refreshResponse.data.refreshToken);
          localStorage.setItem('tokenExpiry', 
            (Date.now() + refreshResponse.data.expiresInSeconds * 1000).toString());
          
          // Update authorization header
          originalRequest.headers.Authorization = `Bearer ${refreshResponse.data.accessToken}`;
          
          isRefreshing = false;
          onRefreshComplete(null);
          
          // Retry the original request
          return api(originalRequest);
        }
      } catch (refreshError) {
        isRefreshing = false;
        onRefreshComplete(refreshError as Error);
        
        // Refresh failed, clear auth state and redirect to login
        localStorage.removeItem('accessToken');
        localStorage.removeItem('refreshToken');
        localStorage.removeItem('tokenExpiry');
        localStorage.removeItem('user');
        window.location.href = '/login';
        return Promise.reject(refreshError);
      }
    }

    // If it's a 401 and we already retried, redirect to login
    if (error.response?.status === 401) {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('tokenExpiry');
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
  
  refresh: async (): Promise<RefreshResponse> => {
    const response = await api.post<RefreshResponse>('/admin/auth/refresh');
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
};

export default api;
