export interface CurrentUser {
  id: number;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  profileName: string;
  permissions: string[];
  mustChangePassword: boolean;
}

export interface SessionResponse {
  accessToken: string;
  user: CurrentUser;
}

export interface User {
  id: number;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  profileId: number;
  profileName: string;
  isActive: boolean;
  isLocked: boolean;
  isPending: boolean;
  mustChangePassword: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface SaveUserRequest {
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  profileId: number;
  isActive: boolean;
}

export interface UserQuery {
  search?: string;
  profileId?: number | null;
  isActive?: boolean | null;
  page: number;
  pageSize: number;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Profile {
  id: number;
  name: string;
  description: string | null;
  isSystem: boolean;
  isDefault: boolean;
  permissions: string[];
  userCount: number;
}

export interface ProfileOption {
  id: number;
  name: string;
}

export interface SaveProfileRequest {
  name: string;
  description: string | null;
  permissions: string[];
}

export interface PermissionInfo {
  code: string;
  group: string;
  label: string;
}

export interface RegisterRequest {
  email: string;
  firstName: string;
  lastName: string;
}

export interface QrCodeStatus {
  enabled: boolean;
  expiresAt: string | null;
}

export interface QrCodeResponse {
  token: string;
  expiresAt: string;
}

export const Permissions = {
  usersRead: 'users.read',
  usersWrite: 'users.write',
  profilesRead: 'profiles.read',
  profilesWrite: 'profiles.write',
} as const;
