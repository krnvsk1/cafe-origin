import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { PosOrderInput } from './pos';
import type { OrderStatus } from '../types';

export interface TableCheckSummary {
    id: number;
    label: string | null;
    openedAt: string;
    guests: number | null;
    waiter: string | null;
    isMine: boolean;
    total: number;
    /** Заказов, где всё готово — пора нести */
    ready: number;
    cooking: number;
}

export interface WaiterTable {
  id: number;
  name: string;
  /** Открытые счета за столом (их может быть несколько) */
  checks: TableCheckSummary[];
}

export interface CheckItem {
  id: number;
  name: string;
  quantity: number;
  price: number;
  addons: { addonName: string; quantity: number; price: number }[];
  readyQuantity: number;
  servedQuantity: number;
  lineTotal: number;
  /** Перенесена из другого счёта при делении */
  moved: boolean;
  /** Сколько штук ждёт решения кухни об отмене */
  pendingVoid: number;
}

export interface CheckVoid {
  id: number;
  name: string;
  quantity: number;
  amount: number;
  reason: string;
  status: 'pending' | 'done' | 'rejected';
  writeOff: boolean | null;
  requestedAt: string;
}

export interface CheckOrder {
  id: number;
  status: OrderStatus;
  total: number;
  notes: string | null;
  createdAt: string;
  items: CheckItem[];
}

export interface TableCheck {
  id: number;
  status: 'open' | 'closed' | 'cancelled';
  label: string | null;
  table: { id: number; name: string };
  /** Другие открытые счета за этим столом */
  siblings: { id: number; label: string | null }[];
  /** Убранные из счёта блюда */
  voids: CheckVoid[];
  waiter: string | null;
  isMine: boolean;
  guests: number | null;
  comment: string | null;
  openedAt: string;
  closedAt: string | null;
  paymentMethod: string | null;
  total: number;
  notServed: number;
  orders: CheckOrder[];
}

const KEY = 'waiter';

export const useWaiterTables = () =>
  useQuery<WaiterTable[]>({
    queryKey: [KEY, 'tables'],
    queryFn: () => apiClient.get('/api/waiter/tables').then(r => r.data),
    refetchInterval: 20_000,
  });

export const useTableCheck = (id: number | null) =>
  useQuery<TableCheck>({
    queryKey: [KEY, 'check', id],
    queryFn: () => apiClient.get(`/api/waiter/checks/${id}`).then(r => r.data),
    enabled: id != null,
    refetchInterval: 15_000,
  });

const useWaiterMutation = <T, R = unknown>(fn: (v: T) => Promise<R>) => {
  const qc = useQueryClient();
  return useMutation<R, Error, T>({
    mutationFn: fn,
    onSettled: () => {
      qc.invalidateQueries({ queryKey: [KEY] });
      qc.invalidateQueries({ queryKey: ['kitchen-orders'] });
    },
  });
};

export const useOpenCheck = () =>
  useWaiterMutation<{ tableId: number; guests: number | null; label?: string | null }, { id: number; alreadyOpen: boolean }>(body =>
    apiClient.post('/api/waiter/checks', body).then(r => r.data));

export const useAddRound = () =>
  useWaiterMutation<{ checkId: number; items: PosOrderInput['items']; comment: string | null }>(({ checkId, ...body }) =>
    apiClient.post(`/api/waiter/checks/${checkId}/orders`, body).then(r => r.data));

export const useCancelRound = () =>
  useWaiterMutation<{ checkId: number; orderId: number }>(({ checkId, orderId }) =>
    apiClient.post(`/api/waiter/checks/${checkId}/orders/${orderId}/cancel`).then(r => r.data));

export const useCloseCheck = () =>
  useWaiterMutation<{ checkId: number; paymentMethod: 'cash' | 'card'; cashReceived: number | null }, { total: number; change: number | null }>(
    ({ checkId, ...body }) => apiClient.post(`/api/waiter/checks/${checkId}/close`, body).then(r => r.data));

export const useCancelCheck = () =>
  useWaiterMutation<number>(id => apiClient.post(`/api/waiter/checks/${id}/cancel`).then(r => r.data));

export const useUpdateCheck = () =>
  useWaiterMutation<{ checkId: number; label: string | null; guests: number | null }>(({ checkId, ...body }) =>
    apiClient.put(`/api/waiter/checks/${checkId}`, body).then(r => r.data));

export const useMoveCheck = () =>
  useWaiterMutation<{ checkId: number; tableId: number }>(({ checkId, tableId }) =>
    apiClient.post(`/api/waiter/checks/${checkId}/move`, { tableId }).then(r => r.data));

export const useSplitCheck = () =>
  useWaiterMutation<{ checkId: number; items: { itemId: number; quantity: number }[]; targetCheckId: number | null; label: string | null }, { targetCheckId: number }>(
    ({ checkId, ...body }) => apiClient.post(`/api/waiter/checks/${checkId}/split`, body).then(r => r.data));

export const useVoidItem = () =>
  useWaiterMutation<{ itemId: number; quantity: number; reason: string }, { id: number; status: string; needsKitchen: boolean }>(
    ({ itemId, ...body }) => apiClient.post(`/api/waiter/items/${itemId}/void`, body).then(r => r.data));

// ==================== Схема зала ====================

export interface FloorWall { id: string; x: number; y: number; w: number; h: number; kind: 'wall' | 'bar' | 'plant' }
export interface FloorTable { tableId: number; x: number; y: number; w: number; h: number; shape: 'rect' | 'round' }
export interface FloorPlan { width: number; height: number; walls: FloorWall[]; tables: FloorTable[] }

export const useFloorPlan = () =>
  useQuery<FloorPlan>({
    queryKey: [KEY, 'floor'],
    queryFn: () => apiClient.get('/api/waiter/floor').then(r => r.data),
    staleTime: 60_000,
  });

export const useSaveFloorPlan = () =>
  useWaiterMutation<FloorPlan, FloorPlan>(plan => apiClient.put('/api/waiter/floor', plan).then(r => r.data));

/** Время с сервера без пояса — это UTC */
export const parseUtc = (iso: string) => new Date(/([zZ]|[+-]\d\d:?\d\d)$/.test(iso) ? iso : `${iso}Z`);

// ==================== Отменённые блюда ====================

export interface VoidRow {
  id: number;
  orderId: number;
  name: string;
  quantity: number;
  amount: number;
  reason: string;
  status: 'pending' | 'done' | 'rejected';
  writeOff: boolean | null;
  orderStatus: string | null;
  requestedAt: string;
  resolvedAt: string | null;
  table: string | null;
  requestedBy: string | null;
  resolvedBy: string | null;
}

export interface VoidsReport {
  from: string;
  to: string;
  summary: { count: number; amount: number; writtenOff: number; pending: number };
  items: VoidRow[];
}

export const useVoidsReport = (from: string, to: string) =>
  useQuery<VoidsReport>({
    queryKey: ['voids', from, to],
    queryFn: () => apiClient.get('/api/voids', { params: { from, to } }).then(r => r.data),
  });

export interface TableSetting { id?: number; name: string; isActive: boolean }

export const useAllTables = (enabled: boolean) =>
  useQuery<(TableSetting & { id: number })[]>({
    queryKey: [KEY, 'all-tables'],
    queryFn: () => apiClient.get('/api/waiter/tables/all').then(r => r.data),
    enabled,
  });

export const useSaveTables = () =>
  useWaiterMutation<TableSetting[]>(list => apiClient.put('/api/waiter/tables', list).then(r => r.data));
