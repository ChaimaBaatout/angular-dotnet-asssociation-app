import { Permissions } from '../../core/models';

export type ContactKind = 'suppliers' | 'customers';

export interface ContactKindInfo {
  /** Page title and navigation label. */
  plural: string;
  /** Lowercase, for use inside sentences. */
  singular: string;
  icon: string;
  read: string;
  write: string;
}

export const CONTACT_KINDS: Record<ContactKind, ContactKindInfo> = {
  suppliers: {
    plural: 'Suppliers',
    singular: 'supplier',
    icon: 'local_shipping',
    read: Permissions.suppliersRead,
    write: Permissions.suppliersWrite,
  },
  customers: {
    plural: 'Customers',
    singular: 'customer',
    icon: 'storefront',
    read: Permissions.customersRead,
    write: Permissions.customersWrite,
  },
};
