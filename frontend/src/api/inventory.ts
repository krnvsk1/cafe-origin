import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { apiClient } from './client';

// ================= Типы =================

export type StockUnit = 'kg' | 'l' | 'pcs';
export type StockItemKind = 'raw' | 'semi';
export type StockDocumentType = 'receipt' | 'writeoff' | 'inventory';
export type StockDocumentStatus = 'draft' | 'posted' | 'cancelled';
export type TechCardTarget = 'dish' | 'addon' | 'semi';

export interface StockItem {
  id: number;
  name: string;
  unit: StockUnit;
  category: string | null;
  kind: StockItemKind;
  quantity: number;
  avgCost: number;
  lastPrice: number;
  minQuantity: number;
  isArchived: boolean;
  unitCost: number;
  value: number;
  isLow: boolean;
  hasTechCard: boolean;
}

export interface StockItemInput {
  name: string;
  unit: StockUnit;
  category?: string | null;
  kind: StockItemKind;
  minQuantity: number;
  lastPrice?: number | null;
  isArchived?: boolean;
}

export interface StockMovement {
  id: number;
  type: 'receipt' | 'writeoff' | 'inventory' | 'sale' | 'sale_return' | 'reversal';
  quantity: number;
  unitCost: number;
  balanceAfter: number;
  documentId: number | null;
  orderId: number | null;
  comment: string | null;
  createdAt: string;
}

export interface Supplier {
  id: number;
  name: string;
  inn: string | null;
  phone: string | null;
  contactPerson: string | null;
  notes: string | null;
  isArchived: boolean;
  receiptsCount: number;
  receiptsTotal: number;
  lastReceiptAt: string | null;
}

export type SupplierInput = Pick<Supplier, 'name' | 'inn' | 'phone' | 'contactPerson' | 'notes'> & { isArchived?: boolean };

export interface InventorySummary {
  itemsCount: number;
  stockValue: number;
  lowCount: number;
  negativeCount: number;
  draftsCount: number;
  dishesCount: number;
  dishCardsCount: number;
}

export interface StockDocumentListItem {
  id: number;
  type: StockDocumentType;
  status: StockDocumentStatus;
  number: string | null;
  date: string;
  supplierId: number | null;
  supplierName: string | null;
  reason: string | null;
  comment: string | null;
  total: number;
  linesCount: number;
  attachmentPath: string | null;
  createdAt: string;
  postedAt: string | null;
}

export interface StockDocumentLine {
  id: number;
  stockItemId: number;
  stockItemName: string | null;
  unit: StockUnit | null;
  currentQuantity: number | null;
  quantity: number;
  price: number;
  sum: number;
  expectedQuantity: number | null;
  difference: number | null;
}

export interface StockDocument extends Omit<StockDocumentListItem, 'linesCount'> {
  cancelledAt: string | null;
  lines: StockDocumentLine[];
}

export interface StockDocumentInput {
  type: StockDocumentType;
  number?: string | null;
  date?: string | null;
  supplierId?: number | null;
  reason?: string | null;
  comment?: string | null;
  lines: { stockItemId: number; quantity: number; price?: number; sum?: number | null }[];
  post?: boolean;
}

export interface InventorySheetRow {
  id: number;
  name: string;
  unit: StockUnit;
  category: string | null;
  quantity: number;
  unitCost: number;
}

export interface DishRow {
  id: number;
  cardId: number;
  name: string;
  dishName: string;
  variantName: string | null;
  category: string;
  price: number;
  isAvailable: boolean;
  techCard: 'own' | 'parent' | 'none';
  cost: number | null;
  margin: number | null;
  marginPercent: number | null;
  foodCostPercent: number | null;
}

export interface AddonRow {
  id: number;
  name: string;
  category: string;
  price: number;
  isAvailable: boolean;
  techCard: 'own' | 'none';
  cost: number | null;
  margin: number | null;
  foodCostPercent: number | null;
}

export interface TechCardLine {
  stockItemId: number;
  stockItemName: string | null;
  unit: StockUnit | null;
  kind: StockItemKind | null;
  gross: number;
  lossPercent: number;
  net: number;
  unitCost: number;
  cost: number;
}

export interface TechCardData {
  id: number;
  outputWeight: number | null;
  yieldQuantity: number;
  instructions: string | null;
  updatedAt: string;
  cost: number;
  unitCost: number;
  margin: number | null;
  foodCostPercent: number | null;
  lines: TechCardLine[];
}

export interface TechCardResponse {
  target: TechCardTarget;
  ownerId: number;
  ownerName: string;
  parentId: number | null;
  price: number | null;
  unit: StockUnit | null;
  exists: boolean;
  card: TechCardData | null;
  inherited: TechCardData | null;
}

export interface TechCardInput {
  outputWeight?: number | null;
  yieldQuantity?: number;
  instructions?: string | null;
  lines: { stockItemId: number; gross: number; lossPercent: number }[];
}

export interface SalesReport {
  from: string;
  to: string;
  rows: {
    shawarmaId: number;
    name: string;
    quantity: number;
    revenue: number;
    cost: number | null;
    margin: number | null;
    foodCostPercent: number | null;
    hasTechCard: boolean;
  }[];
  totals: {
    revenue: number;
    revenueWithCards: number;
    cost: number;
    margin: number;
    foodCostPercent: number | null;
    withoutCardCount: number;
    withoutCardQuantity: number;
  };
}

export interface ConsumptionReport {
  from: string;
  to: string;
  rows: {
    stockItemId: number;
    name: string;
    unit: StockUnit;
    category: string | null;
    received: number;
    sold: number;
    soldValue: number;
    writtenOff: number;
    writtenOffValue: number;
    inventoryDiff: number;
    inventoryDiffValue: number;
    reversals: number;
    balance: number;
  }[];
  totals: { soldValue: number; writtenOffValue: number; inventoryDiffValue: number };
}

// ================= Подписи и форматирование =================

export const UNIT_LABELS: Record<StockUnit, string> = { kg: 'кг', l: 'л', pcs: 'шт' };

export const DOCUMENT_TITLES: Record<StockDocumentType, { one: string; many: string; create: string }> = {
  receipt: { one: 'Приход', many: 'Приходы', create: 'Новый приход' },
  writeoff: { one: 'Списание', many: 'Списания', create: 'Новое списание' },
  inventory: { one: 'Инвентаризация', many: 'Инвентаризации', create: 'Новая инвентаризация' },
};

export const STATUS_LABELS: Record<StockDocumentStatus, string> = {
  draft: 'Черновик',
  posted: 'Проведён',
  cancelled: 'Отменён',
};

export const WRITEOFF_REASONS: Record<string, string> = {
  spoilage: 'Порча',
  expired: 'Истёк срок',
  staff: 'Питание персонала',
  damage: 'Бой, брак',
  other: 'Другое',
};

export const MOVEMENT_LABELS: Record<StockMovement['type'], string> = {
  receipt: 'Приход',
  writeoff: 'Списание',
  inventory: 'Инвентаризация',
  sale: 'Продажа',
  sale_return: 'Возврат по заказу',
  reversal: 'Отмена документа',
};

const qtyFormat = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 });
const moneyFormat = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 0, maximumFractionDigits: 2 });

export const fmtQty = (value: number | null | undefined, unit?: StockUnit | null) =>
  value === null || value === undefined ? '—' : `${qtyFormat.format(value)}${unit ? ` ${UNIT_LABELS[unit]}` : ''}`;

export const fmtMoney = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : `${moneyFormat.format(value)} ₽`;

export const fmtDate = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleDateString('ru-RU', { day: 'numeric', month: 'short', year: 'numeric' }) : '—';

export const fmtDateTime = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleString('ru-RU', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '—';

/** Текст ошибки из ответа API */
export const apiError = (error: unknown, fallback = 'Что-то пошло не так') => {
  const e = error as { response?: { status?: number; data?: { message?: string; Message?: string } }; message?: string };
  if (e?.response?.status === 403) return 'Недостаточно прав — попросите администратора добавить их вашей роли';
  return e?.response?.data?.message || e?.response?.data?.Message || e?.message || fallback;
};

/** Дата для input[type=date] */
export const toDateInput = (iso?: string | null) => {
  const d = iso ? new Date(iso) : new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
};

// ================= Запросы =================

const KEY = 'inventory';

export const useInventorySummary = () =>
  useQuery<InventorySummary>({
    queryKey: [KEY, 'summary'],
    queryFn: () => apiClient.get('/api/inventory/summary').then(r => r.data),
  });

export const useStockItems = (params: { search?: string; kind?: StockItemKind; category?: string; lowOnly?: boolean; includeArchived?: boolean } = {}) =>
  useQuery<StockItem[]>({
    queryKey: [KEY, 'items', params],
    queryFn: () => apiClient.get('/api/inventory/items', { params }).then(r => r.data),
    placeholderData: keepPreviousData,
  });

export const useStockCategories = () =>
  useQuery<string[]>({
    queryKey: [KEY, 'categories'],
    queryFn: () => apiClient.get('/api/inventory/items/categories').then(r => r.data),
  });

export const useStockMovements = (itemId: number | null) =>
  useQuery<StockMovement[]>({
    queryKey: [KEY, 'movements', itemId],
    queryFn: () => apiClient.get(`/api/inventory/items/${itemId}/movements`, { params: { limit: 200 } }).then(r => r.data),
    enabled: !!itemId,
  });

export const useSaveStockItem = () => {
  const qc = useQueryClient();
  return useMutation<StockItem, Error, { id?: number; data: StockItemInput }>({
    mutationFn: ({ id, data }) =>
      (id ? apiClient.put(`/api/inventory/items/${id}`, data) : apiClient.post('/api/inventory/items', data)).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

export const useDeleteStockItem = () => {
  const qc = useQueryClient();
  return useMutation<{ archived: boolean; message?: string }, Error, number>({
    mutationFn: (id) => apiClient.delete(`/api/inventory/items/${id}`).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

export const useSuppliers = (includeArchived = false) =>
  useQuery<Supplier[]>({
    queryKey: [KEY, 'suppliers', includeArchived],
    queryFn: () => apiClient.get('/api/inventory/suppliers', { params: { includeArchived } }).then(r => r.data),
  });

export const useSaveSupplier = () => {
  const qc = useQueryClient();
  return useMutation<Supplier, Error, { id?: number; data: SupplierInput }>({
    mutationFn: ({ id, data }) =>
      (id ? apiClient.put(`/api/inventory/suppliers/${id}`, data) : apiClient.post('/api/inventory/suppliers', data)).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [KEY, 'suppliers'] }),
  });
};

export const useDeleteSupplier = () => {
  const qc = useQueryClient();
  return useMutation<{ archived: boolean; message?: string }, Error, number>({
    mutationFn: (id) => apiClient.delete(`/api/inventory/suppliers/${id}`).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [KEY, 'suppliers'] }),
  });
};

export interface DocumentFilters {
  type: StockDocumentType;
  status?: StockDocumentStatus | '';
  search?: string;
  from?: string;
  to?: string;
  page?: number;
}

export const useStockDocuments = (filters: DocumentFilters) =>
  useQuery<{ items: StockDocumentListItem[]; total: number; page: number; pageSize: number }>({
    queryKey: [KEY, 'documents', filters],
    queryFn: () =>
      apiClient.get('/api/inventory/documents', {
        params: { ...filters, status: filters.status || undefined, search: filters.search || undefined },
      }).then(r => r.data),
    placeholderData: keepPreviousData,
  });

export const useStockDocument = (id: number | null) =>
  useQuery<StockDocument>({
    queryKey: [KEY, 'document', id],
    queryFn: () => apiClient.get(`/api/inventory/documents/${id}`).then(r => r.data),
    enabled: !!id,
  });

export const fetchInventorySheet = (category?: string) =>
  apiClient.get<InventorySheetRow[]>('/api/inventory/documents/inventory-sheet', { params: { category } }).then(r => r.data);

export const useSaveStockDocument = () => {
  const qc = useQueryClient();
  return useMutation<StockDocument, Error, { id?: number; data: StockDocumentInput }>({
    mutationFn: ({ id, data }) =>
      (id ? apiClient.put(`/api/inventory/documents/${id}`, data) : apiClient.post('/api/inventory/documents', data)).then(r => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

export const usePostStockDocument = () => {
  const qc = useQueryClient();
  return useMutation<StockDocument, Error, number>({
    mutationFn: (id) => apiClient.post(`/api/inventory/documents/${id}/post`).then(r => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

export const useCancelStockDocument = () => {
  const qc = useQueryClient();
  return useMutation<StockDocument, Error, number>({
    mutationFn: (id) => apiClient.post(`/api/inventory/documents/${id}/cancel`).then(r => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

export const useDeleteStockDocument = () => {
  const qc = useQueryClient();
  return useMutation<unknown, Error, number>({
    mutationFn: (id) => apiClient.delete(`/api/inventory/documents/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

export const uploadDocumentAttachment = (id: number, file: File) => {
  const form = new FormData();
  form.append('file', file);
  return apiClient
    .post<{ attachmentPath: string }>(`/api/inventory/documents/${id}/attachment`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
    .then(r => r.data);
};

export const deleteDocumentAttachment = (id: number) => apiClient.delete(`/api/inventory/documents/${id}/attachment`);

export const useDishes = () =>
  useQuery<{ dishes: DishRow[]; addons: AddonRow[] }>({
    queryKey: [KEY, 'dishes'],
    queryFn: () => apiClient.get('/api/inventory/dishes').then(r => r.data),
  });

export const useTechCard = (target: TechCardTarget | null, id: number | null) =>
  useQuery<TechCardResponse>({
    queryKey: [KEY, 'techcard', target, id],
    queryFn: () => apiClient.get(`/api/inventory/techcards/${target}/${id}`).then(r => r.data),
    enabled: !!target && !!id,
  });

export const fetchTechCard = (target: TechCardTarget, id: number) =>
  apiClient.get<TechCardResponse>(`/api/inventory/techcards/${target}/${id}`).then(r => r.data);

export const useSaveTechCard = () => {
  const qc = useQueryClient();
  return useMutation<TechCardResponse, Error, { target: TechCardTarget; id: number; data: TechCardInput }>({
    mutationFn: ({ target, id, data }) => apiClient.put(`/api/inventory/techcards/${target}/${id}`, data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

export const useDeleteTechCard = () => {
  const qc = useQueryClient();
  return useMutation<unknown, Error, { target: TechCardTarget; id: number }>({
    mutationFn: ({ target, id }) => apiClient.delete(`/api/inventory/techcards/${target}/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

export const useSalesReport = (from: string, to: string) =>
  useQuery<SalesReport>({
    queryKey: [KEY, 'report', 'sales', from, to],
    queryFn: () => apiClient.get('/api/inventory/reports/sales', { params: { from, to } }).then(r => r.data),
    placeholderData: keepPreviousData,
  });

export const useConsumptionReport = (from: string, to: string) =>
  useQuery<ConsumptionReport>({
    queryKey: [KEY, 'report', 'consumption', from, to],
    queryFn: () => apiClient.get('/api/inventory/reports/consumption', { params: { from, to } }).then(r => r.data),
    placeholderData: keepPreviousData,
  });

export const recalculateStock = () => apiClient.post<{ changed: number }>('/api/inventory/recalculate').then(r => r.data);
