import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';

export interface DeliveryZone {
  id: number;
  name: string;
  polygon: [number, number][];
  deliveryPrice: number;
  minOrderAmount: number;
  freeDeliveryFrom: number | null;
  deliveryMinutes: number;
  color: string;
  isActive: boolean;
  sortOrder: number;
}

export type DeliveryZoneInput = Omit<DeliveryZone, 'id'>;

export interface ZoneCheck {
  deliverable: boolean;
  message?: string;
  zoneId?: number;
  zoneName?: string;
  deliveryPrice?: number;
  minOrderAmount?: number;
  freeDeliveryFrom?: number | null;
  deliveryMinutes?: number;
  minOrderReached?: boolean;
}

export const useDeliveryZones = () =>
  useQuery<DeliveryZone[]>({
    queryKey: ['delivery-zones'],
    queryFn: () => apiClient.get('/api/delivery-zones').then(res => res.data),
    staleTime: 5 * 60 * 1000,
  });

export const useAllDeliveryZones = () =>
  useQuery<DeliveryZone[]>({
    queryKey: ['delivery-zones', 'all'],
    queryFn: () => apiClient.get('/api/delivery-zones/all').then(res => res.data),
  });

export const checkDeliveryZone = (latitude: number, longitude: number, subtotal: number) =>
  apiClient.post<ZoneCheck>('/api/delivery-zones/check', { latitude, longitude, subtotal }).then(res => res.data);

export const useSaveDeliveryZone = () => {
  const queryClient = useQueryClient();
  return useMutation<DeliveryZone, Error, { id?: number; data: DeliveryZoneInput }>({
    mutationFn: ({ id, data }) =>
      (id ? apiClient.put(`/api/delivery-zones/${id}`, data) : apiClient.post('/api/delivery-zones', data)).then(res => res.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['delivery-zones'] }),
  });
};

export const useDeleteDeliveryZone = () => {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, number>({
    mutationFn: (id) => apiClient.delete(`/api/delivery-zones/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['delivery-zones'] }),
  });
};

/** Общая минимальная сумма заказа на доставку (для любого адреса; у зоны может быть своя, больше) */
export interface DeliveryRules { minOrderAmount: number }

export const useDeliveryRules = () =>
  useQuery<DeliveryRules>({
    queryKey: ['delivery-zones', 'rules'],
    queryFn: () => apiClient.get('/api/delivery-zones/rules').then(res => res.data),
    staleTime: 5 * 60 * 1000,
  });

/** Минимальная сумма заказа: только для доставки (из «Зон доставки»); самовывоз и «в зале» — без минимума */
export const useMinOrder = (deliveryType: string) => {
  const { data } = useDeliveryRules();
  return deliveryType === 'Доставка' ? data?.minOrderAmount ?? 0 : 0;
};

export const useSaveDeliveryRules = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (r: DeliveryRules) => apiClient.put('/api/delivery-zones/rules', r).then(res => res.data as DeliveryRules),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['delivery-zones'] }),
  });
};
