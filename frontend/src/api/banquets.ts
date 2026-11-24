import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { FiscalResult } from './waiter';

export interface BanquetSummary {
  id: number;
  customerName: string;
  phone: string | null;
  eventAt: string;
  guests: number | null;
  status: 'open' | 'closed' | 'cancelled';
  kitchenAt: string | null;
  table: string | null;
  total: number;
  prepaid: number;
  dishes: number;
  unsent: number;
}

export interface BanquetTotals { dishes: number; discount: number; service: number; total: number; prepaid: number; due: number }

export interface BanquetItem {
  id: number; dishId: number; name: string; price: number; quantity: number; weight: number | null; comment: string | null;
  addons: { addonId: number; quantity: number; name: string; price: number }[];
  sent: boolean;
  kitchenStatus: string | null;
}

export interface BanquetPayment {
  id: number;
  kind: 'prepay' | 'final';
  method: 'cash' | 'card' | 'prepaid';
  amount: number;
  createdAt: string;
  refundedAt: string | null;
  createdBy: string | null;
  fiscal: { id: number; kind: 'sell' | 'return'; status: 'processing' | 'done' | 'error'; error: string | null; fiscalDocumentNumber: number | null; sold: boolean } | null;
}

export interface Banquet {
  id: number;
  customerName: string;
  phone: string | null;
  eventAt: string;
  guests: number | null;
  tableId: number | null;
  table: string | null;
  comment: string | null;
  status: 'open' | 'closed' | 'cancelled';
  kitchenAt: string | null;
  serviceType: 'percent' | 'amount' | null;
  serviceValue: number | null;
  discountType: 'percent' | 'amount' | null;
  discountValue: number | null;
  discountBy: string | null;
  createdAt: string;
  createdBy: string | null;
  closedAt: string | null;
  closedBy: string | null;
  totals: BanquetTotals;
  items: BanquetItem[];
  payments: BanquetPayment[];
  canDiscount: boolean;
  fiscalEnabled: boolean;
  printEnabled: boolean;
}

export interface BanquetInput {
  customerName: string;
  phone: string | null;
  eventAt: string;
  guests: number | null;
  tableId: number | null;
  comment: string | null;
  kitchenAt: string | null;
  serviceType: 'percent' | 'amount' | null;
  serviceValue: number | null;
}

export interface BanquetItemInput {
  dishId: number; quantity: number; weight?: number; comment?: string;
  addons: { addonId: number; quantity: number }[];
}

const KEY = 'banquets';

export const useBanquets = (from: string, to: string) =>
  useQuery<BanquetSummary[]>({
    queryKey: [KEY, 'list', from, to],
    queryFn: () => apiClient.get('/api/banquets', { params: { from, to } }).then(r => r.data),
    refetchInterval: 60_000,
  });

export const useBanquet = (id: number | null) =>
  useQuery<Banquet>({
    queryKey: [KEY, 'one', id],
    queryFn: () => apiClient.get(`/api/banquets/${id}`).then(r => r.data),
    enabled: id != null,
    refetchInterval: 30_000,
  });

const useBanquetMutation = <T, R = unknown>(fn: (v: T) => Promise<R>) => {
  const qc = useQueryClient();
  return useMutation<R, Error, T>({
    mutationFn: fn,
    onSettled: () => {
      qc.invalidateQueries({ queryKey: [KEY] });
      qc.invalidateQueries({ queryKey: ['waiter'] });
    },
  });
};

export const useCreateBanquet = () => useBanquetMutation<BanquetInput, { id: number }>(b => apiClient.post('/api/banquets', b).then(r => r.data));
export const useUpdateBanquet = () => useBanquetMutation<{ id: number } & BanquetInput>(({ id, ...b }) => apiClient.put(`/api/banquets/${id}`, b).then(r => r.data));
export const useBanquetDiscount = () =>
  useBanquetMutation<{ id: number; type: string | null; value: number | null }>(({ id, ...b }) => apiClient.put(`/api/banquets/${id}/discount`, b).then(r => r.data));
export const useAddBanquetItems = () =>
  useBanquetMutation<{ id: number; items: BanquetItemInput[] }>(({ id, items }) => apiClient.post(`/api/banquets/${id}/items`, { items }).then(r => r.data));
export const useSetBanquetItemQty = () =>
  useBanquetMutation<{ id: number; itemId: number; quantity: number }>(({ id, itemId, quantity }) => apiClient.put(`/api/banquets/${id}/items/${itemId}`, { quantity }).then(r => r.data));
export const useRemoveBanquetItem = () =>
  useBanquetMutation<{ id: number; itemId: number }>(({ id, itemId }) => apiClient.delete(`/api/banquets/${id}/items/${itemId}`).then(r => r.data));
export const useSendBanquetToKitchen = () => useBanquetMutation<number>(id => apiClient.post(`/api/banquets/${id}/kitchen`).then(r => r.data));
export const usePrintBanquet = () => useBanquetMutation<number, { jobId: number }>(id => apiClient.post(`/api/banquets/${id}/print`).then(r => r.data));

export interface BanquetFiscalResult { fiscal: FiscalResult | null; fiscalSkipped?: 'disabled' | 'recorded' | 'empty' | null }
export const usePrepayBanquet = () =>
  useBanquetMutation<{ id: number; amount: number; method: 'cash' | 'card' }, BanquetFiscalResult>(({ id, ...b }) => apiClient.post(`/api/banquets/${id}/prepay`, b).then(r => r.data));
export const useRefundBanquetPayment = () =>
  useBanquetMutation<{ id: number; paymentId: number }, { returned: FiscalResult | null }>(({ id, paymentId }) => apiClient.post(`/api/banquets/${id}/payments/${paymentId}/refund`).then(r => r.data));
export const useCloseBanquet = () =>
  useBanquetMutation<{ id: number; method: 'cash' | 'card' }, BanquetFiscalResult & { total: number; prepaid: number; due: number }>(
    ({ id, ...b }) => apiClient.post(`/api/banquets/${id}/close`, b).then(r => r.data));
export const useCancelBanquet = () =>
  useBanquetMutation<{ id: number; reason: string }, { returned: number; done: number; pending: number }>(({ id, reason }) => apiClient.post(`/api/banquets/${id}/cancel`, { reason }).then(r => r.data));
export const useRetryBanquetReceipt = () => useBanquetMutation<number, FiscalResult>(receiptId => apiClient.post(`/api/banquets/receipts/${receiptId}/retry`).then(r => r.data));
