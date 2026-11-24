import { useAuthStore } from '../store/authStore';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from './client';
import { saveOrderToken, getOrderToken } from '../utils/orderTokens';
import type { 
  Dish, 
  CreateDishDto,
  Order, 
  CreateOrderDto, 
  UpdateOrderDto,
  UpdateOrderStatusDto,
  OrderStats,
  HealthStatus,
  DashboardStats,
  DishImage,
  User,
  UserDetail,
  AssignRoleDto,
  UsersQueryParams,
  PromoCodeValidation,
  PromoCode,
  PointsBalance,
  PointsTransaction,
  RedeemPointsRequest,
  RedeemPointsResponse,
  AdminGrantPointsRequest,
  RestaurantSettings,
} from '../types';

// ==================== DISH HOOKS ====================

export const useDishes = () => {
  return useQuery<Dish[]>({
    queryKey: ['dishes'],
    queryFn: () => apiClient.get('/api/dishes').then(res => res.data),
    staleTime: 5 * 60 * 1000,
    retry: 2,
  });
};

export const useDish = (id: number) => {
  return useQuery<Dish>({
    queryKey: ['dish', id],
    queryFn: () => apiClient.get(`/api/dishes/${id}`).then(res => res.data),
    enabled: !!id,
  });
};

export const useCreateDish = () => {
  const queryClient = useQueryClient();
  return useMutation<Dish, Error, CreateDishDto>({
    mutationFn: (data) => apiClient.post('/api/dishes', data).then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dishes'] });
    },
  });
};

export const useUpdateDish = () => {
  const queryClient = useQueryClient();
  return useMutation<Dish, Error, Partial<Dish> & { id: number }>({
    mutationFn: ({ id, ...data }) => 
      apiClient.put(`/api/dishes/${id}`, data).then(res => res.data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['dishes'] });
      queryClient.invalidateQueries({ queryKey: ['dish', variables.id] });
    },
  });
};

export const useDeleteDish = () => {
  const queryClient = useQueryClient();
  return useMutation<void, Error, number>({
    mutationFn: (id) => apiClient.delete(`/api/dishes/${id}`).then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dishes'] });
    },
  });
};

// 👇 Новый хук для обновления только доступности
export const useUpdateDishAvailability = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async ({ id, isAvailable }: { id: number; isAvailable: boolean }) => {
      // Сначала получаем текущий товар
      const { data: dish } = await apiClient.get(`/api/dishes/${id}`);
      // Обновляем только поле isAvailable
      const updated = { ...dish, isAvailable };
      await apiClient.put(`/api/dishes/${id}`, updated);
    },
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['dishes'] });
      queryClient.invalidateQueries({ queryKey: ['dish', variables.id] });
    },
  });
};

// Порядок карточек; category — перенести карточку (с вариантами) в другой раздел
export const useUpdateDishOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (items: { id: number; sortOrder: number; category?: string }[]) => {
      await apiClient.put('/api/dishes/reorder', items);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dishes'] });
    },
  });
};

// ==================== CATEGORY HOOK ====================

export const useCategories = () => {
  return useQuery<string[]>({
    queryKey: ['dish-categories'],
    queryFn: () => apiClient.get('/api/dishes/categories').then(res => res.data),
    staleTime: 5 * 60 * 1000,
  });
};

// ==================== ORDER HOOKS ====================

// Заказы за период (только для персонала); незавершённые приходят всегда
export const useOrders = (period: { from: string; to: string }, enabled = true) => {
  return useQuery<Order[]>({
    queryKey: ['orders', period.from, period.to],
    queryFn: () => apiClient.get('/api/orders', { params: period }).then(res => res.data),
    staleTime: 2 * 60 * 1000,
    enabled,
  });
};

// Мои заказы (для обычных пользователей)
export const useMyOrders = (enabled = true) => {
  return useQuery<Order[]>({
    queryKey: ['my-orders'],
    queryFn: () => apiClient.get('/api/orders/my').then(res => res.data),
    staleTime: 2 * 60 * 1000,
    enabled,
  });
};

export const useOrder = (id: number) => {
  return useQuery<Order>({
    queryKey: ['order', id],
    // Гость открывает свой заказ по токену, сохранённому при оформлении
    queryFn: () => apiClient
      .get(`/api/orders/${id}`, { params: { token: getOrderToken(id) ?? undefined } })
      .then(res => res.data),
    enabled: !!id,
  });
};

export const useCreateOrder = () => {
  const queryClient = useQueryClient();
  return useMutation<Order & { confirmationUrl?: string | null }, Error, CreateOrderDto>({
    // Бэкенд отвечает { order, pointsEarned, accessToken, confirmationUrl } — достаём сам заказ
    mutationFn: (data) => apiClient.post('/api/orders', data).then(res => {
      const order: Order = res.data?.order ?? res.data;
      saveOrderToken(order.id, res.data?.accessToken);
      return { ...order, confirmationUrl: res.data?.confirmationUrl ?? null };
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['orders'] });
      queryClient.invalidateQueries({ queryKey: ['my-orders'] });
      // Баллы списываются при создании заказа — обновляем баланс и историю
      queryClient.invalidateQueries({ queryKey: ['points-balance'] });
      queryClient.invalidateQueries({ queryKey: ['points-history'] });
    },
  });
};

export const useUpdateOrderStatus = () => {
  const queryClient = useQueryClient();
  return useMutation<Order, Error, { id: number; status: UpdateOrderStatusDto }>({
    mutationFn: ({ id, status }) => 
      apiClient.patch(`/api/orders/${id}/status`, status).then(res => res.data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['orders'] });
      queryClient.invalidateQueries({ queryKey: ['order', variables.id] });
      queryClient.invalidateQueries({ queryKey: ['my-orders'] });
      queryClient.invalidateQueries({ queryKey: ['points-balance'] });
      queryClient.invalidateQueries({ queryKey: ['points-history'] });
    },
  });
};

// Полное обновление заказа
export const useUpdateOrder = () => {
  const queryClient = useQueryClient();
  return useMutation<Order, Error, { id: number; data: UpdateOrderDto }>({
    mutationFn: ({ id, data }) => 
      apiClient.put(`/api/orders/${id}`, data).then(res => res.data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['orders'] });
      queryClient.invalidateQueries({ queryKey: ['order', variables.id] });
      queryClient.invalidateQueries({ queryKey: ['order-stats'] });
      queryClient.invalidateQueries({ queryKey: ['my-orders'] });
      queryClient.invalidateQueries({ queryKey: ['points-balance'] });
      queryClient.invalidateQueries({ queryKey: ['points-history'] });
    },
  });
};

// Удаление заказа
export const useDeleteOrder = () => {
  const queryClient = useQueryClient();
  return useMutation<{ message: string; deletedId: number }, Error, number>({
    mutationFn: (id) => apiClient.delete(`/api/orders/${id}`).then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['orders'] });
      queryClient.invalidateQueries({ queryKey: ['order-stats'] });
    },
  });
};

// Статистика заказов
export const useOrderStats = (period: { from: string; to: string }) => {
  return useQuery<OrderStats>({
    queryKey: ['order-stats', period.from, period.to],
    queryFn: () => apiClient.get('/api/orders/stats', { params: period }).then(res => res.data),
    staleTime: 60 * 1000,
  });
};

// ==================== DASHBOARD HOOKS ====================

export const useDashboardStats = () => {
  return useQuery<DashboardStats>({
    queryKey: ['dashboard'],
    queryFn: () => apiClient.get('/api/dashboard/stats').then(res => res.data),
    staleTime: 60 * 1000,
  });
};

// ==================== HEALTH HOOK ====================

export const useHealth = () => {
  return useQuery<HealthStatus>({
    queryKey: ['health'],
    queryFn: () => apiClient.get('/api/health').then(res => res.data),
    refetchInterval: 30 * 1000,
  });
};

// ==================== IMAGE HOOKS ====================

export const useUploadImage = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async ({ dishId, file }: { dishId: number; file: File }) => {
      const formData = new FormData();
      formData.append('file', file);
      
      const { data } = await apiClient.post(`/api/image/upload/${dishId}`, formData, {
        headers: {
          'Content-Type': 'multipart/form-data',
        },
      });
      return data;
    },
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['dish', variables.dishId] });
      queryClient.invalidateQueries({ queryKey: ['dish-images', variables.dishId] });
    },
  });
};

export const useDishImages = (dishId: number) => {
  return useQuery({
    queryKey: ['dish-images', dishId],
    queryFn: async () => {
      const { data } = await apiClient.get(`/api/image/dish/${dishId}`);
      return data as DishImage[];
    },
    enabled: !!dishId,
  });
};

export const useDeleteImage = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (imageId: number) => {
      await apiClient.delete(`/api/image/${imageId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dishes'] });
    },
  });
};

// 👇 Оставил, но он используется только в старом MenuPage
// Если везде убрали перетаскивание — можно удалить
export const useReorderDishes = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (items: { id: number; order: number }[]) => {
      const response = await apiClient.put('/api/dishes/reorder', items);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dishes'] });
    },
  });
};

// ==================== USER HOOKS ====================

// Получить список пользователей
export const useUsers = (params?: UsersQueryParams) => {
  return useQuery<User[]>({
    queryKey: ['users', params],
    queryFn: async () => {
      const queryParams = new URLSearchParams();
      if (params?.role) queryParams.append('role', params.role);
      if (params?.page) queryParams.append('page', params.page.toString());
      if (params?.pageSize) queryParams.append('pageSize', params.pageSize.toString());
      
      const { data } = await apiClient.get(`/api/users?${queryParams.toString()}`);
      return data;
    },
    staleTime: 2 * 60 * 1000,
  });
};

// Получить пользователя по ID
export const useUser = (id: number) => {
  return useQuery<UserDetail>({
    queryKey: ['user', id],
    queryFn: () => apiClient.get(`/api/users/${id}`).then(res => res.data),
    enabled: !!id,
  });
};

// Назначить роль пользователю
export const useAssignRole = () => {
  const queryClient = useQueryClient();
  
  return useMutation<{ message: string }, Error, { userId: number; role: AssignRoleDto }>({
    mutationFn: ({ userId, role }) => 
      apiClient.post(`/api/users/${userId}/roles`, role).then(res => res.data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      queryClient.invalidateQueries({ queryKey: ['user', variables.userId] });
    },
  });
};

// Удалить роль у пользователя
export const useRemoveRole = () => {
  const queryClient = useQueryClient();
  
  return useMutation<{ message: string }, Error, { userId: number; role: string }>({
    mutationFn: ({ userId, role }) => 
      apiClient.delete(`/api/users/${userId}/roles/${role}`).then(res => res.data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      queryClient.invalidateQueries({ queryKey: ['user', variables.userId] });
    },
  });
};

// ==================== PROMO CODE HOOKS ====================

export const useValidatePromoCode = () => {
  return useMutation<PromoCodeValidation, Error, { code: string; orderAmount: number }>({
    mutationFn: ({ code, orderAmount }) =>
      apiClient.post('/api/promocodes/validate', { code, orderAmount }).then(res => res.data),
  });
};

export const usePromoCodes = () => {
  return useQuery<PromoCode[]>({
    queryKey: ['promocodes'],
    queryFn: () => apiClient.get('/api/promocodes').then(res => res.data),
    enabled: false, // Only for admin, enabled manually
  });
};

export const useCreatePromoCode = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: Partial<PromoCode>) =>
      apiClient.post('/api/promocodes', data).then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['promocodes'] });
    },
  });
};

export const useUpdatePromoCode = () => {
  const queryClient = useQueryClient();
  return useMutation<PromoCode, Error, { id: number; data: Partial<PromoCode> }>({
    mutationFn: ({ id, data }) =>
      apiClient.put(`/api/promocodes/${id}`, data).then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['promocodes'] });
    },
  });
};

export const useDeletePromoCode = () => {
  const queryClient = useQueryClient();
  return useMutation<void, Error, number>({
    mutationFn: (id) => apiClient.delete(`/api/promocodes/${id}`).then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['promocodes'] });
    },
  });
};

// ==================== LOYALTY POINTS HOOKS ====================

export const usePointsBalance = () => {
  // Баланс есть только у вошедших — гостю не спрашиваем (раньше это давало 401)
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  return useQuery<PointsBalance>({
    queryKey: ['points-balance'],
    queryFn: () => apiClient.get('/api/points/balance').then(res => ({
      balance: res.data.balance ?? res.data.Balance ?? 0
    })),
    enabled: isAuthenticated,
    staleTime: 30 * 1000,
  });
};

export const usePointsHistory = (page = 1, pageSize = 20) => {
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  return useQuery<PointsTransaction[]>({
    queryKey: ['points-history', page, pageSize],
    queryFn: async () => {
      const { data } = await apiClient.get(`/api/points/history?page=${page}&pageSize=${pageSize}`);
      return data.transactions || data.Transactions || [];
    },
    enabled: isAuthenticated,
    staleTime: 30 * 1000,
  });
};

export const useUserPointsBalance = (userId: number) => {
  return useQuery<{ userId: number; balance: number }>({
    queryKey: ['user-points-balance', userId],
    queryFn: () => apiClient.get(`/api/points/users/${userId}/balance`).then(res => res.data),
    enabled: !!userId,
  });
};

export const useAdminGrantPoints = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: AdminGrantPointsRequest) =>
      apiClient.post('/api/points/admin/grant', data).then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['points-balance'] });
      queryClient.invalidateQueries({ queryKey: ['users'] });
      queryClient.invalidateQueries({ queryKey: ['user-points-balance'] });
    },
  });
};

export const useRedeemPoints = () => {
  return useMutation<RedeemPointsResponse, Error, RedeemPointsRequest>({
    mutationFn: (data) =>
      apiClient.post('/api/points/redeem', data).then(res => res.data),
  });
};
// ==================== SETTINGS ====================

export const useRestaurantSettings = () => {
  return useQuery<RestaurantSettings>({
    queryKey: ['restaurant-settings'],
    queryFn: () => apiClient.get('/api/settings').then(res => res.data),
    staleTime: 5 * 60 * 1000,
  });
};

export const DEFAULT_BRAND = 'Доставка еды';
export const DEFAULT_TAGLINE = 'Привезём горячим или соберём к вашему приходу';

/** Название заведения и подзаголовок; пока настройки грузятся — нейтральные */
export const useBrand = () => {
  const { data } = useRestaurantSettings();
  return { name: data?.brandName || DEFAULT_BRAND, tagline: data?.brandTagline || DEFAULT_TAGLINE, loaded: !!data };
};

export const useSaveBrand = () => {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, { name: string; tagline: string | null }>({
    mutationFn: (data) => apiClient.put('/api/settings/brand', data).then(res => res.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['restaurant-settings'] }),
  });
};

// ==================== PAYMENTS (ЮKassa) ====================

export const usePaymentStatus = (orderId: number, enabled: boolean) => {
  return useQuery<{ id: number; paymentMethod: string; paymentStatus: string; status: string }>({
    queryKey: ['payment-status', orderId],
    queryFn: () => apiClient
      .get(`/api/payments/orders/${orderId}/status`, { params: { token: getOrderToken(orderId) ?? undefined } })
      .then(res => res.data),
    enabled: enabled && !!orderId,
    // Пока платёж в обработке — проверяем каждые 3 секунды
    refetchInterval: (query) => (query.state.data?.paymentStatus === 'pending' ? 3000 : false),
  });
};

export const useStartPayment = () => {
  return useMutation<{ confirmationUrl: string }, Error, number>({
    mutationFn: (orderId) => apiClient
      .post(`/api/payments/orders/${orderId}/pay`, null, { params: { token: getOrderToken(orderId) ?? undefined } })
      .then(res => res.data),
    onSuccess: (data) => {
      window.location.href = data.confirmationUrl;
    },
  });
};

// Разделы меню (Пиццы, Бургеры…) — для выбора при создании позиции
export const useMenuSections = () => {
  return useQuery<string[]>({
    queryKey: ['menu-sections'],
    queryFn: () => apiClient.get('/api/dishes/sections').then(res => res.data),
    staleTime: 60 * 1000,
  });
};
