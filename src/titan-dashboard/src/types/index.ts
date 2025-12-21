// Admin User Types
export interface AdminUser {
  id: string;
  email: string;
  displayName: string | null;
  roles: string[];
  createdAt: string;
  lastLoginAt: string | null;
}

export interface CreateAdminUserRequest {
  email: string;
  password: string;
  displayName?: string;
  roles: string[];
}

export interface UpdateAdminUserRequest {
  displayName?: string;
  roles: string[];
}

// Auth Types
export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  success: boolean;
  userId: string;
  email: string;
  displayName: string | null;
  roles: string[];
  sessionId: string;
  expiresAt: string;  // ISO 8601
}

// Account Types
export interface AccountSummary {
  accountId: string;
  createdAt: string;
  lastModified: string;
  characterCount: number;
}

export interface AccountDetail {
  accountId: string;
  createdAt: string;
  unlockedCosmetics: string[];
  unlockedAchievements: string[];
}

export interface CharacterSummary {
  characterId: string;
  name: string;
  seasonId: string;
  level: number;
  isDead: boolean;
  restrictions: string;
  createdAt: string;
}

// Season Types
export const SeasonType = {
  Permanent: 0,
  Temporary: 1,
} as const;
export type SeasonType = (typeof SeasonType)[keyof typeof SeasonType];

export const SeasonStatus = {
  Upcoming: 0,
  Active: 1,
  Ended: 2,
} as const;
export type SeasonStatus = (typeof SeasonStatus)[keyof typeof SeasonStatus];

export interface Season {
  seasonId: string;
  name: string;
  type: SeasonType;
  status: SeasonStatus;
  startDate: string;
  endDate: string | null;
  migrationTargetId: string;
  isVoid: boolean;
}

export interface CreateSeasonRequest {
  seasonId: string;
  name: string;
  type?: SeasonType;
  status?: SeasonStatus;
  startDate: string;
  endDate?: string;
  migrationTargetId?: string;
  isVoid?: boolean;
}

// Base Type Types - Must match C# Titan.Abstractions.Models.Items.Enums
export const ItemCategory = {
  Currency: 0,
  Equipment: 1,
  Gem: 2,
  Map: 3,
  Consumable: 4,
  Material: 5,
  Quest: 6,
} as const;
export type ItemCategory = (typeof ItemCategory)[keyof typeof ItemCategory];

// Must match C# Titan.Abstractions.Models.Items.EquipmentSlot
export const EquipmentSlot = {
  None: 0,
  MainHand: 1,
  OffHand: 2,
  Helmet: 3,
  BodyArmour: 4,
  Gloves: 5,
  Boots: 6,
  Belt: 7,
  Amulet: 8,
  RingLeft: 9,
  RingRight: 10,
} as const;
export type EquipmentSlot = (typeof EquipmentSlot)[keyof typeof EquipmentSlot];


export interface BaseType {
  baseTypeId: string;
  name: string;
  description: string | null;
  category: ItemCategory;
  slot: EquipmentSlot;
  width: number;
  height: number;
  maxStackSize: number;
  isTradeable: boolean;
}

export interface CreateBaseTypeRequest {
  baseTypeId: string;
  name: string;
  description?: string;
  category?: ItemCategory;
  slot?: EquipmentSlot;
  width?: number;
  height?: number;
  maxStackSize?: number;
  isTradeable?: boolean;
}

// Rate Limiting Types
export interface RateLimitRule {
  maxHits: number;
  periodSeconds: number;
  timeoutSeconds: number;
}

export interface RateLimitPolicy {
  name: string;
  rules: RateLimitRule[];
}

export interface EndpointRateLimitConfig {
  pattern: string;
  policyName: string;
}

export interface RateLimitingConfiguration {
  enabled: boolean;
  defaultPolicyName: string;
  policies: RateLimitPolicy[];
  endpointMappings: EndpointRateLimitConfig[];
}

// Rate Limiting Metrics Types
export interface RateLimitBucket {
  partitionKey: string;
  policyName: string;
  periodSeconds: number;
  currentCount: number;
  secondsRemaining: number;
}

export interface RateLimitTimeout {
  partitionKey: string;
  policyName: string;
  secondsRemaining: number;
}

export interface RateLimitMetrics {
  activeBuckets: number;
  activeTimeouts: number;
  buckets: RateLimitBucket[];
  timeouts: RateLimitTimeout[];
}

// Session Types
export interface SessionInfo {
  ticketId: string;
  userId: string;
  provider: string;
  roles: string[];
  createdAt: string;
  expiresAt: string;
  lastActivityAt: string;
  isAdmin: boolean;
}

export interface SessionListResponse {
  sessions: SessionInfo[];
  totalCount: number;
  skip: number;
  take: number;
}
