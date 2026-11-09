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
  /** Счёт закрыт на сотрудника (без оплаты и без чека) */
  staffName?: string | null;
  staffReason?: string | null;
  /** Оплату меняли после закрытия: какая была, кто и когда */
  paymentWas?: string | null;
  paymentChangedBy?: string | null;
  paymentChangedAt?: string | null;
  /** Менеджер может исправить оплаченный счёт на сотрудника (смена открыта) */
  canFixToStaff?: boolean;
  /** Скидка: сумма блюд, скидка, тип и значение, можно ли давать */
  subtotal?: number;
  discount?: number;
  discountType?: 'percent' | 'amount' | null;
  discountValue?: number | null;
  canDiscount?: boolean;
  total: number;
  notServed: number;
  /** Принтер счетов включён в настройках */
  printEnabled?: boolean;
  /** Когда печатали счёт для гостя и на какую сумму (дозаказали — печатать заново) */
  printedAt?: string | null;
  printedTotal?: number | null;
  /** Онлайн-касса АТОЛ включена */
  fiscalEnabled?: boolean;
  /** Кассовый чек на АТОЛ по закрытому счёту */
  fiscal?: { status: 'processing' | 'done' | 'error'; error: string | null; fiscalDocumentNumber: number | null } | null;
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

export type CheckPayment = 'cash' | 'card' | 'staff';

/** Способ оплаты счёта словами */
export const payLabel = (m: string | null | undefined) =>
  m === 'cash' ? 'наличные' : m === 'card' ? 'карта' : m === 'staff' ? 'на заведение' : m ?? '';

export interface CloseCheckResult {
  total: number;
  change: number | null;
  fiscal: FiscalResult | null;
  /** Почему чек в АТОЛ не отправлялся: staff — закрыт на сотрудника, disabled — онлайн-касса выключена */
  fiscalSkipped?: 'staff' | 'disabled' | null;
  staff?: string | null;
  printJobId?: number | null;
  printError?: string | null;
}

export const useCloseCheck = () =>
  useWaiterMutation<{ checkId: number; paymentMethod: CheckPayment; cashReceived: number | null; staffUserId?: number | null; staffReason?: string | null }, CloseCheckResult>(
    ({ checkId, ...body }) => apiClient.post(`/api/waiter/checks/${checkId}/close`, body).then(r => r.data));

/** Сменить способ оплаты закрытого счёта: если чек уже в АТОЛ — возврат и новый чек */
export interface ChangePaymentResult {
  paymentMethod: CheckPayment;
  /** Чек возврата прежней оплаты (если чек был пробит) */
  returned: FiscalResult | null;
  /** Новый чек продажи */
  fiscal: FiscalResult | null;
  fiscalSkipped?: 'disabled' | 'staff' | null;
  staff?: string | null;
}
export const useChangePayment = () =>
  useWaiterMutation<{ checkId: number; paymentMethod: CheckPayment; staffUserId?: number | null; staffReason?: string | null }, ChangePaymentResult>(({ checkId, ...body }) =>
    apiClient.post(`/api/waiter/checks/${checkId}/payment`, body).then(r => r.data));

/** Закрытие на сотрудника: включено ли, можно ли мне, кого выбрать */
export interface StaffChecksInfo {
  enabled: boolean;
  allowed: boolean;
  managersOnly: boolean;
  staff: { id: number; name: string }[] | null;
  reasons: string[];
  /** Кто закрывает — сотрудник по умолчанию */
  me: number | null;
}
export const useStaffChecksInfo = (enabled: boolean) =>
  useQuery<StaffChecksInfo>({
    queryKey: [KEY, 'staff-checks'],
    queryFn: () => apiClient.get('/api/waiter/staff-checks').then(r => r.data),
    enabled,
    staleTime: 60_000,
  });

export const useCheckDiscount = () =>
  useWaiterMutation<{ checkId: number; type: string | null; value: number | null }>(({ checkId, type, value }) =>
    apiClient.put(`/api/waiter/checks/${checkId}/discount`, { type, value }).then(r => r.data));

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
  mine: { id: number; openedAt: string; openChecks: number; approvedBy?: string | null } | null;
  /** Мой запрос на смену ждёт подтверждения менеджера */
  minePending?: { id: number; requestedAt: string } | null;
  /** Могу подтверждать смены официантов */
  canApprove?: boolean;
  /** Запросы официантов на открытие смены */
  pendingWaiters?: { id: number; userId: number; name: string | null; requestedAt: string }[] | null;
  waiters: { id: number; userId: number; name: string | null; openedAt: string; openChecks: number; approvedBy?: string | null }[] | null;
  totalOpenChecks: number | null;
  /** Непробитые кассовые чеки за 2 недели (видят менеджеры; null — касса выключена) */
  unfiscalized?: number | null;
}

export const useShiftCurrent = () =>
  useQuery<ShiftCurrent>({
    queryKey: [KEY, 'shift'],
    queryFn: () => apiClient.get('/api/shifts/current').then(r => r.data),
    // Пока ждём подтверждения смены (или кто-то ждёт нашего) — проверяем чаще
    refetchInterval: q => (q.state.data?.minePending || q.state.data?.pendingWaiters?.length ? 8_000 : 30_000),
  });

/** Смена заведения; если включена онлайн-касса — смена на АТОЛ открывается/закрывается вместе с ней */
export interface VenueShiftResult { id: number; fiscal: { ok: boolean; message: string } | null }
export const useOpenVenueShift = () => useWaiterMutation<void, VenueShiftResult>(() => apiClient.post('/api/shifts/venue/open').then(r => r.data));
export const useCloseVenueShift = () => useWaiterMutation<void, VenueShiftResult>(() => apiClient.post('/api/shifts/venue/close').then(r => r.data));
/** Итоги текущей смены заведения — показываются перед закрытием */
export interface VenueReport {
  id: number; businessDate: string; openedAt: string;
  hall: { checks: number; cash: number; card: number; staff: number; refunds: number; open: number; openTotal: number };
  site: { orders: number; cash: number; card: number; online: number; staff: number; refunds: number };
  banquets: { payments: number; cash: number; card: number; staff: number };
  totals: { cash: number; card: number; online: number; staff: number; refunds: number; revenue: number };
}
export const useVenueReport = (enabled: boolean) =>
  useQuery<VenueReport>({
    queryKey: ['venue-report'],
    queryFn: () => apiClient.get('/api/shifts/venue/report').then(r => r.data),
    enabled,
    staleTime: 0,
  });
export const useOpenMyShift = () => useWaiterMutation<void, { id: number; pending: boolean }>(() => apiClient.post('/api/shifts/mine/open').then(r => r.data));
export const useApproveShift = () => useWaiterMutation<number>(id => apiClient.post(`/api/shifts/waiters/${id}/approve`).then(r => r.data));
export const useRejectShift = () => useWaiterMutation<number>(id => apiClient.post(`/api/shifts/waiters/${id}/reject`).then(r => r.data));
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
  /** За счёт заведения — не выручка */
  staff?: number;
  /** Возвраты: деньги вернули гостю — не выручка */
  refunds?: number;
}

export interface ShiftDetails {
  id: number;
  businessDate: string;
  openedAt: string;
  closedAt: string | null;
  openedBy: string | null;
  closedBy: string | null;
  waiters: { id: number; userId: number; name: string | null; openedAt: string; closedAt: string | null; checks: number; revenue: number; approvedBy?: string | null }[];
  /** Закрыто на сотрудников: к удержанию по людям */
  staff?: { userId: number; name: string; checks: number; total: number }[];
  checks: {
    id: number; table: string | null; label: string | null; waiter: string | null; waiterId: number | null;
    status: 'open' | 'closed' | 'cancelled'; guests: number | null; openedAt: string; closedAt: string | null;
    paymentMethod: string | null; total: number;
    staffName?: string | null; staffReason?: string | null;
    paymentWas?: string | null; paymentChangedBy?: string | null; paymentChangedAt?: string | null;
    fiscal: CheckFiscalState | null;
    fiscalReturn: CheckFiscalState | null;
    /** Деньги вернули гостю (чек возврата пробит) — счёт не в выручке */
    refunded?: boolean;
    /** Заказы кухни по этому счёту */
    orderIds?: number[];
    refundedBy?: string | null;
    refundedAt?: string | null;
  }[];
}

export interface CheckFiscalState { status: 'processing' | 'done' | 'error'; error: string | null; fiscalDocumentNumber: number | null }

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

/** Напечатать счёт для гостя (пречек) на принтере счетов; вернёт задание печати */
export const usePrintCheck = () =>
  useWaiterMutation<number, { jobId: number }>(checkId =>
    apiClient.post(`/api/waiter/checks/${checkId}/print`).then(r => r.data));

/** Пробить (повторить) кассовый чек на АТОЛ по закрытому счёту */
export const useCheckFiscal = () =>
  useWaiterMutation<number, FiscalResult>(checkId => apiClient.post(`/api/waiter/checks/${checkId}/fiscal`).then(r => r.data));

export interface FiscalResult { status: 'processing' | 'done' | 'error'; error: string | null; fiscalDocumentNumber?: number | null }

/** Чек возврата по счёту стола (гость вернул деньги) — те же позиции и сумма, что в чеке продажи */
/** Возврат счёта «на заведение» (официант ошибся): без денег и чека, только пометка */
export const useHouseRefund = () =>
  useWaiterMutation<number, { id: number; refundedAt: string }>(checkId => apiClient.post(`/api/waiter/checks/${checkId}/house-refund`).then(r => r.data));

export const useReturnCheckFiscal = () =>
  useWaiterMutation<number, FiscalResult>(checkId => apiClient.post(`/api/waiter/checks/${checkId}/fiscal/return`).then(r => r.data));

/** Кассовый чек, который не пробился или по которому касса не ответила */
export interface UnfinishedReceipt {
  id: number; orderId: number | null; checkId: number | null; banquetId?: number | null; banquetPaymentId?: number | null;
  kind: 'sell' | 'sellReturn'; status: 'processing' | 'error';
  error: string | null; total: number; paymentMethod: string | null; createdAt: string; table: string | null; label: string | null;
}

export const useUnfinishedReceipts = (enabled: boolean) =>
  useQuery<UnfinishedReceipt[]>({
    queryKey: [KEY, 'fiscal-unfinished'],
    queryFn: () => apiClient.get('/api/shifts/fiscal/unfinished').then(r => r.data),
    enabled,
    refetchInterval: 60_000,
  });

/** Повторить или проверить непробитый чек: по счёту стола — через официанта, по заказу кассы — через кассу */
export const useRetryReceipt = () =>
  useWaiterMutation<UnfinishedReceipt, FiscalResult>(r => {
    if (r.banquetPaymentId != null) return apiClient.post(`/api/banquets/receipts/${r.id}/retry`).then(x => x.data);
    if (r.checkId != null) {
      const url = r.kind === 'sell' ? `/api/waiter/checks/${r.checkId}/fiscal` : `/api/waiter/checks/${r.checkId}/fiscal/return`;
      return apiClient.post(url).then(x => x.data);
    }
    return apiClient.post(`/api/fiscal/orders/${r.orderId}/${r.kind === 'sell' ? 'sell' : 'return'}`).then(x => x.data);
  });

export interface PrintJobState { id: number; status: 'pending' | 'taken' | 'done' | 'error'; error: string | null; agentOnline: boolean }

export const fetchPrintJob = (id: number) =>
  apiClient.get<PrintJobState>(`/api/waiter/print-jobs/${id}`).then(r => r.data);

/** Поправить вес весового блюда (официант уточнил, повар взвесил) — сумма пересчитается */
export const useSetItemWeight = () =>
  useWaiterMutation<{ itemId: number; grams: number }>(({ itemId, grams }) =>
    apiClient.put(`/api/kitchen/items/${itemId}/weight`, { grams }).then(r => r.data));

export interface MyChecks {
  shift: { id: number; openedAt: string; closedAt: string | null } | null;
  summary: { closed: number; revenue: number; cash: number; card: number; staff?: number; open: number };
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

// ==================== Отмена всего счёта и заказы с сайта ====================

export interface CancelAllResult { cancelled: boolean; done: number; pending: number; returned?: FiscalResult | null }

/** Менеджер отменяет весь открытый счёт: блюда по правилам отмены (кухня подтверждает начатые) */
export const useCancelCheckAll = () =>
  useWaiterMutation<{ checkId: number; reason: string }, CancelAllResult>(({ checkId, reason }) =>
    apiClient.post(`/api/waiter/checks/${checkId}/cancel-all`, { reason }).then(r => r.data));

export interface SiteOrder {
  id: number;
  createdAt: string;
  customerName: string;
  phone: string;
  deliveryType: string;
  address: string;
  status: OrderStatus;
  paymentMethod: string;
  paymentStatus: string;
  total: number;
  deliveryPrice?: number;
  closedAt: string | null;
  closedBy: string | null;
  staffName: string | null;
  staffReason: string | null;
  pendingVoids: number;
  canClose: boolean;
  fiscal: { kind: 'sell' | 'return'; status: 'processing' | 'done' | 'error'; error: string | null; fiscalDocumentNumber: number | null; sold: boolean } | null;
}

export const useSiteOrders = (enabled: boolean) =>
  useQuery<SiteOrder[]>({
    queryKey: [KEY, 'site-orders'],
    queryFn: () => apiClient.get('/api/site-orders').then(r => r.data),
    enabled,
    refetchInterval: 30_000,
  });

export interface SiteCloseResult { fiscal: FiscalResult | null; fiscalSkipped?: 'staff' | 'disabled' | 'yookassa' | null; staff?: string | null }

export const useCloseSiteOrder = () =>
  useWaiterMutation<{ id: number; paymentMethod: string; staffUserId?: number | null; staffReason?: string | null; discountType?: string | null; discountValue?: number | null }, SiteCloseResult>(({ id, ...body }) =>
    apiClient.post(`/api/site-orders/${id}/close`, body).then(r => r.data));

export const useSiteOrderFiscal = () =>
  useWaiterMutation<number, FiscalResult>(id => apiClient.post(`/api/site-orders/${id}/fiscal`).then(r => r.data));

export const useCancelSiteOrder = () =>
  useWaiterMutation<{ id: number; reason: string }, CancelAllResult>(({ id, reason }) =>
    apiClient.post(`/api/site-orders/${id}/cancel`, { reason }).then(r => r.data));

/** Что ушло в АТОЛ — одной строкой для уведомления */
export const atolText = (fiscal: FiscalResult | null | undefined, skipped?: string | null, _staff?: string | null) =>
  fiscal
    ? fiscal.status === 'done' ? `Чек отправлен в АТОЛ и пробит${fiscal.fiscalDocumentNumber ? ` (ФД ${fiscal.fiscalDocumentNumber})` : ''}`
      : fiscal.status === 'processing' ? 'Чек отправлен в АТОЛ, касса ещё печатает'
      : `Чек в АТОЛ не пробит: ${fiscal.error ?? 'ошибка кассы'}`
    : skipped === 'staff' ? 'В АТОЛ не отправлялся — на заведение'
    : skipped === 'yookassa' ? 'Оплачен онлайн — чек отправила ЮKassa'
    : 'В АТОЛ не отправлялся — онлайн-касса выключена';
