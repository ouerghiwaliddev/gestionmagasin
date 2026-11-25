export type Role = 'admin' | 'manager' | 'viewer';
export type MovementType = 'IN' | 'OUT';

export interface User {
  id: number;
  username: string;
  displayName: string;
  role: Role;
  active: boolean;
  dashboardPeriod: string;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface Product {
  id: number;
  name: string;
  sku: string;
  description: string;
  currentStock: number;
  threshold: number;
  status: string;
  isCritical: boolean;
  isLow: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface Movement {
  id: number;
  productId: number;
  productName: string;
  productSku: string;
  movementType: MovementType;
  quantityChange: number;
  unitPrice: number;
  taxRate: number;
  totalHt: number;
  taxAmount: number;
  totalTtc: number;
  supplierId: number | null;
  supplierName: string | null;
  agentId: number | null;
  agentName: string | null;
  serviceId: number | null;
  serviceName: string | null;
  legacyText: string;
  reason: string;
  recordedBy: string;
  timestamp: string;
}

export interface Alert {
  id: number;
  productId: number;
  productName: string;
  productSku: string;
  alertType: string;
  message: string;
  isResolved: boolean;
  createdAt: string;
}

export interface Reference {
  id: number;
  name: string;
  active: boolean;
}

export interface ReferenceSection {
  kind: 'supplier' | 'agent' | 'service';
  singular: string;
  plural: string;
  icon: string;
  items: Reference[];
}

export interface Lookups {
  suppliers: Reference[];
  agents: Reference[];
  services: Reference[];
  taxRates: number[];
  defaultTaxRate: number;
}

export interface Page<T> {
  items: T[];
  page: number;
  pages: number;
  total: number;
  perPage: number;
}

export interface DashboardStats {
  totalProducts: number;
  criticalProducts: number;
  lowStockProducts: number;
  recentMovements: number;
  entryCount: number;
  entryQuantity: number;
  entryValue: number;
  exitCount: number;
  exitQuantity: number;
  exitValue: number;
  activeAlerts: number;
  period: string;
  periodLabel: string;
  periodStart: string | null;
  periodEnd: string;
}

export interface Dashboard {
  stats: DashboardStats;
  products: Product[];
  alerts: Alert[];
}

export interface InventoryItem {
  productId: number;
  sku: string;
  name: string;
  stock: number;
  entryQuantity: number;
  cumulQuantity: number;
  cumulTotalTtc: number;
  cmp: number | null;
  stockValue: number | null;
}

export interface Inventory {
  year: number;
  years: number[];
  items: InventoryItem[];
  totalValue: number;
  missingCmp: number;
}

export interface AuditLog {
  id: number;
  actorId: number | null;
  actorUsername: string;
  action: string;
  targetType: string;
  targetId: string | null;
  details: { before?: unknown; after?: unknown };
  createdAt: string;
}

export interface MovementFilters {
  productId: string;
  dateFrom: string;
  dateTo: string;
  movementType: string;
  agentId: string;
  supplierId: string;
  serviceId: string;
}

export const PERIODS: { key: string; label: string }[] = [
  { key: 'always', label: 'Tous' },
  { key: 'yearly', label: 'Annuel' },
  { key: 'quarterly', label: 'Trimestriel' },
  { key: 'monthly', label: 'Mensuel' },
];
