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

export interface ArticleOption {
  id: number;
  reference: string;
  name: string;
  brand: string | null;
  salePrice: number;
  quantity: number;
}

export type PaymentStatus = 'NotPaid' | 'PartiallyPaid' | 'Paid';

export const PAYMENT_STATUSES: { value: PaymentStatus; label: string }[] = [
  { value: 'NotPaid', label: 'Not paid' },
  { value: 'PartiallyPaid', label: 'Partially paid' },
  { value: 'Paid', label: 'Paid' },
];

export function paymentLabel(status: PaymentStatus): string {
  return PAYMENT_STATUSES.find((s) => s.value === status)!.label;
}

/** Orders have no stored number; it is formed from the id, and the API search accepts it. */
export function orderNumber(id: number): string {
  return `ORD-${String(id).padStart(5, '0')}`;
}

/** The rate the API applies to new orders. Must match OrdersController.VatRate. */
export const VAT_RATE = 0.19;

export interface OrderSummary {
  id: number;
  orderDate: string;
  customerId: number | null;
  customerName: string | null;
  lineCount: number;
  total: number;
  paymentStatus: PaymentStatus;
  amountPaid: number;
  amountDue: number;
  createdAt: string;
}

export interface OrderCustomer {
  id: number;
  name: string;
  address: string | null;
  city: string | null;
  phone: string | null;
  email: string | null;
  taxNumber: string | null;
}

export interface OrderLine {
  id: number;
  articleId: number;
  reference: string;
  name: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface Order {
  id: number;
  orderDate: string;
  customer: OrderCustomer | null;
  notes: string | null;
  lines: OrderLine[];
  vatRate: number;
  subtotal: number;
  vatAmount: number;
  total: number;
  paymentStatus: PaymentStatus;
  amountPaid: number;
  amountDue: number;
  createdAt: string;
  updatedAt: string;
}

export interface SavePaymentRequest {
  paymentStatus: PaymentStatus;
  /** Only read for a partial payment. */
  amountPaid: number;
}

export interface SaveOrderRequest extends SavePaymentRequest {
  orderDate: string;
  customerId: number | null;
  notes: string | null;
  lines: { articleId: number; quantity: number; unitPrice: number }[];
}

export interface OrderQuery {
  search?: string;
  paymentStatus?: PaymentStatus | null;
  customerId?: number | null;
  page: number;
  pageSize: number;
}

export interface OrderStats {
  outstanding: number;
  amountDue: number;
}

export interface Company {
  name: string;
  address: string | null;
  phone: string | null;
  email: string | null;
  taxNumber: string | null;
}

/** Prices are stored with 3 decimals, as for millimes. */
export const CURRENCY = 'TND';

/** Rounds to millimes the way the API does, half away from zero. */
export function roundMoney(value: number): number {
  return Math.sign(value) * Math.round(Math.abs(value) * 1000 + Number.EPSILON) / 1000;
}

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
  ordersRead: 'orders.read',
  ordersWrite: 'orders.write',
} as const;
