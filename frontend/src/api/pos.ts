import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { FiscalReceiptResult } from './fiscal';

export interface PosOrderInput {
  items: { dishId: number; quantity: number; addons: { addonId: number; quantity: number }[]; comment?: string }[];
  orderType: 'В зале' | 'Самовывоз';
  paymentMethod: 'cash' | 'card';
  cashReceived?: number | null;
  customerName?: string | null;
  phone?: string | null;
  comment?: string | null;
  sendToKitchen: boolean;
}

export interface PosOrderResult {
  id: number;
  total: number;
  status: string;
  paymentMethod: string;
  change: number | null;
  customerFound: boolean;
  /** Чек на кассе АТОЛ; null — касса не подключена */
  fiscal: FiscalReceiptResult | null;
}

export interface PosOrder {
  id: number;
  status: string;
  total: number;
  customerName: string;
  phone: string;
  deliveryType: string;
  paymentMethod: string;
  paymentStatus: string;
  source: string;
  notes: string | null;
  createdAt: string;
  completedAt: string | null;
  /** Последний чек по заказу на кассе АТОЛ */
  fiscal: { kind: 'sell' | 'sellReturn'; status: 'processing' | 'done' | 'error'; error: string | null; fiscalDocumentNumber: number | null } | null;
  /** Чек прихода пробит */
  sold: boolean;
  items: { name: string; quantity: number; price: number; addons: { addonName: string; quantity: number; price: number }[] }[];
}

export interface PosSummary {
  count: number;
  total: number;
  cash: number;
  card: number;
  averageCheck: number;
}

export const useCreatePosOrder = () => {
  const qc = useQueryClient();
  return useMutation<PosOrderResult, Error, PosOrderInput>({
    mutationFn: (data) => apiClient.post('/api/pos/orders', data).then(r => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['pos'] });
      qc.invalidateQueries({ queryKey: ['orders'] });
    },
  });
};

export const usePosOrders = (all: boolean) =>
  useQuery<PosOrder[]>({
    queryKey: ['pos', 'orders', all],
    queryFn: () => apiClient.get('/api/pos/orders', { params: { all } }).then(r => r.data),
    refetchInterval: 15000,
  });

export const usePosSummary = () =>
  useQuery<PosSummary>({
    queryKey: ['pos', 'summary'],
    queryFn: () => apiClient.get('/api/pos/summary').then(r => r.data),
    refetchInterval: 30000,
  });

export const lookupPosCustomer = (phone: string) =>
  apiClient.get<{ found: boolean; name?: string; points?: number }>('/api/pos/customer', { params: { phone } }).then(r => r.data);

export const useSetOrderStatus = () => {
  const qc = useQueryClient();
  return useMutation<unknown, Error, { id: number; status: string }>({
    mutationFn: ({ id, status }) => apiClient.patch(`/api/orders/${id}/status`, { status }).then(r => r.data),
    onSettled: () => {
      qc.invalidateQueries({ queryKey: ['pos'] });
      qc.invalidateQueries({ queryKey: ['orders'] });
    },
  });
};
