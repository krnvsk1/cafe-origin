import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { OrderStatus } from '../types';

// ==================== КУХНЯ ====================

export interface KitchenOrder {
  id: number;
  status: OrderStatus;
  deliveryType: string;
  customerName: string;
  notes: string | null;
  total: number;
  createdAt: string;
  scheduledFor: string | null;
  isLater: boolean;
  items: KitchenOrderItem[];
}

export interface KitchenOrderItem {
  shawarmaId: number;
  name: string;
  quantity: number;
  addons: { addonId: number; addonName: string; quantity: number }[];
}

export interface KitchenTechCardLine {
  stockItemId: number | null;
  /** Блюдо в составе (unit = 'portion', gross — доля порции) */
  dishId?: number | null;
  isDish?: boolean;
  name: string;
  unit: string;
  isSemi: boolean;
  gross: number;
  net: number;
  lossPercent: number;
}

export interface KitchenTechCard {
  title: string;
  outputWeight: number | null;
  yieldQuantity: number;
  instructions: string | null;
  lines: KitchenTechCardLine[];
}

export interface KitchenTechCardResponse {
  title: string;
  dish: KitchenTechCard | null;
  addons: KitchenTechCard[];
  semis: KitchenTechCard[];
}

/** Техкарта блюда и выбранных добавок для повара (без себестоимости); грузится только когда окно открыто */
export const useKitchenTechCard = (item: KitchenOrderItem | null) =>
  useQuery<KitchenTechCardResponse>({
    queryKey: ['kitchen-techcard', item?.shawarmaId, item?.addons.map(a => a.addonId).join(',')],
    queryFn: () => apiClient
      .get(`/api/kitchen/techcard/${item!.shawarmaId}`, { params: { addons: item!.addons.map(a => a.addonId).join(',') || undefined } })
      .then(res => res.data),
    enabled: !!item,
    staleTime: 5 * 60 * 1000,
  });

export const useKitchenOrders = () =>
  useQuery<KitchenOrder[]>({
    queryKey: ['kitchen-orders'],
    queryFn: () => apiClient.get('/api/kitchen/orders').then(res => res.data),
    // Подстраховка на случай обрыва живого потока
    refetchInterval: 60 * 1000,
  });

export const useSetOrderStatus = () => {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, { id: number; status: OrderStatus }>({
    mutationFn: ({ id, status }) => apiClient.patch(`/api/orders/${id}/status`, { status }).then(res => res.data),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['kitchen-orders'] });
      queryClient.invalidateQueries({ queryKey: ['orders'] });
    },
  });
};

// ==================== КУРЬЕР ====================

export interface CourierOrder {
  id: number;
  status: OrderStatus;
  customerName: string;
  phone: string;
  address: string;
  notes: string | null;
  total: number;
  createdAt: string;
  scheduledFor: string | null;
  pickedUpAt: string | null;
  paymentMethod: string;
  amountToCollect: number;
  itemsCount: number;
  items: string[];
}

export const useCourierOrders = () =>
  useQuery<{ available: CourierOrder[]; mine: CourierOrder[] }>({
    queryKey: ['courier-orders'],
    queryFn: () => apiClient.get('/api/courier/orders').then(res => res.data),
    refetchInterval: 60 * 1000,
  });

export type CourierAction = 'take' | 'release' | 'pickup' | 'delivered';

export const useCourierAction = () => {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, { id: number; action: CourierAction }>({
    mutationFn: ({ id, action }) => apiClient.post(`/api/courier/orders/${id}/${action}`).then(res => res.data),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['courier-orders'] });
      queryClient.invalidateQueries({ queryKey: ['orders'] });
    },
  });
};
