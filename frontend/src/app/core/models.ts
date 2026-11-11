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

export interface Contact {
  id: number;
  name: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  city: string | null;
  taxNumber: string | null;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface SaveContactRequest {
  name: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  city: string | null;
  taxNumber: string | null;
  notes: string | null;
  isActive: boolean;
}

export interface ContactQuery {
  search?: string;
  isActive?: boolean | null;
  page: number;
  pageSize: number;
}

export interface ContactOption {
  id: number;
  name: string;
}

export interface Article {
  id: number;
  reference: string;
  name: string;
  brand: string | null;
  category: string | null;
  compatibility: string | null;
  description: string | null;
  location: string | null;
  supplierId: number | null;
  supplierName: string | null;
  purchasePrice: number;
  salePrice: number;
  quantity: number;
  minimumStock: number;
  isLowStock: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface SaveArticleRequest {
  reference: string;
  name: string;
  brand: string | null;
  category: string | null;
  compatibility: string | null;
  description: string | null;
  location: string | null;
  supplierId: number | null;
  purchasePrice: number;
  salePrice: number;
  quantity: number;
  minimumStock: number;
  isActive: boolean;
}

export interface ArticleQuery {
  search?: string;
  category?: string | null;
  supplierId?: number | null;
  lowStock?: boolean | null;
  isActive?: boolean | null;
  page: number;
  pageSize: number;
}

export interface ArticleStats {
  total: number;
  lowStock: number;
  outOfStock: number;
  stockValue: number;
}

/** Prices are stored with 3 decimals, as for millimes. */
export const CURRENCY = 'TND';

export const Permissions = {
  usersRead: 'users.read',
  usersWrite: 'users.write',
  profilesRead: 'profiles.read',
  profilesWrite: 'profiles.write',
  articlesRead: 'articles.read',
  articlesWrite: 'articles.write',
  suppliersRead: 'suppliers.read',
  suppliersWrite: 'suppliers.write',
  customersRead: 'customers.read',
  customersWrite: 'customers.write',
} as const;
