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
    /** Счёт другого официанта: видно только, что стол занят */
    hidden: boolean;
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
  /** Вес весового блюда, г — можно поправить, пока счёт открыт */
  weight?: number | null;
  price: number;
  addons: { addonName: string; quantity: number; price: number }[];
  readyQuantity: number;
  servedQuantity: number;
  lineTotal: number;
  /** Перенесена из другого счёта при делении */
  moved: boolean;
  /** Сколько штук ждёт решения кухни об отмене */
  pendingVoid: number;
  /** Статус блюда на его кухне: Новый, Принят, Готовится, Готов, Отдано, Отменён */
  kitchenStatus: string;
  /** Кухня блюда — когда кухонь несколько */
  station: string | null;
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
  waiterId: number | null;
  isMine: boolean;
  /** Менеджер: видит все счета, может передать счёт другому официанту */
  canManage: boolean;
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

// ==================== Смены ====================

export interface ShiftCurrent {
  canManageVenue: boolean;
  seesAll: boolean;
  venue: { id: number; businessDate: string; openedAt: string; openedBy: string | null; stale: boolean } | null;
  mine: { id: number; openedAt: string; openChecks: number } | null;
  waiters: { id: number; userId: number; name: string | null; openedAt: string; openChecks: number }[] | null;
  totalOpenChecks: number | null;
}

export const useShiftCurrent = () =>
  useQuery<ShiftCurrent>({
    queryKey: [KEY, 'shift'],
    queryFn: () => apiClient.get('/api/shifts/current').then(r => r.data),
    refetchInterval: 30_000,
  });

export const useOpenVenueShift = () => useWaiterMutation<void>(() => apiClient.post('/api/shifts/venue/open').then(r => r.data));
export const useCloseVenueShift = () => useWaiterMutation<void>(() => apiClient.post('/api/shifts/venue/close').then(r => r.data));
export const useOpenMyShift = () => useWaiterMutation<void>(() => apiClient.post('/api/shifts/mine/open').then(r => r.data));
export const useCloseMyShift = () => useWaiterMutation<void>(() => apiClient.post('/api/shifts/mine/close').then(r => r.data));

export const useAssignCheck = () =>
  useWaiterMutation<{ id: number; waiterId: number }>(({ id, waiterId }) => apiClient.post(`/api/waiter/checks/${id}/assign`, { waiterId }).then(r => r.data));

export interface ShiftRow {
  id: number;
  businessDate: string;
  openedAt: string;
  closedAt: string | null;
  openedBy: string | null;
  closedBy: string | null;
  waiters: number;
  checks: number;
  openChecks: number;
  revenue: number;
  cash: number;
  card: number;
}

export interface ShiftDetails {
  id: number;
  businessDate: string;
  openedAt: string;
  closedAt: string | null;
  openedBy: string | null;
  closedBy: string | null;
  waiters: { id: number; userId: number; name: string | null; openedAt: string; closedAt: string | null; checks: number; revenue: number }[];
  checks: {
    id: number; table: string | null; label: string | null; waiter: string | null; waiterId: number | null;
    status: 'open' | 'closed' | 'cancelled'; guests: number | null; openedAt: string; closedAt: string | null;
    paymentMethod: string | null; total: number;
  }[];
}

export const useShifts = (from: string, to: string) =>
  useQuery<ShiftRow[]>({
    queryKey: [KEY, 'shifts', from, to],
    queryFn: () => apiClient.get('/api/shifts', { params: { from, to } }).then(r => r.data),
  });

export const useShiftDetails = (id: number | null) =>
  useQuery<ShiftDetails>({
    queryKey: [KEY, 'shifts', 'one', id],
    queryFn: () => apiClient.get(`/api/shifts/${id}`).then(r => r.data),
    enabled: id != null,
  });

// ==================== Отдать блюдо, мои чеки ====================

/** Официант отдал гостю готовое блюдо (delta null — все готовые штуки) */
export const useServeItem = () =>
  useWaiterMutation<{ itemId: number; delta: number | null }>(({ itemId, delta }) =>
    apiClient.post(`/api/kitchen/items/${itemId}/served`, { delta }).then(r => r.data));

/** Поправить вес весового блюда (официант уточнил, повар взвесил) — сумма пересчитается */
export const useSetItemWeight = () =>
  useWaiterMutation<{ itemId: number; grams: number }>(({ itemId, grams }) =>
    apiClient.put(`/api/kitchen/items/${itemId}/weight`, { grams }).then(r => r.data));

export interface MyChecks {
  shift: { id: number; openedAt: string; closedAt: string | null } | null;
  summary: { closed: number; revenue: number; cash: number; card: number; open: number };
  checks: {
    id: number; table: string | null; label: string | null; status: 'open' | 'closed' | 'cancelled'; guests: number | null;
    openedAt: string; closedAt: string | null; paymentMethod: string | null; total: number;
  }[];
}

export const useMyChecks = (enabled: boolean) =>
  useQuery<MyChecks>({
    queryKey: [KEY, 'my-checks'],
    queryFn: () => apiClient.get('/api/waiter/my-checks').then(r => r.data),
    enabled,
  });
