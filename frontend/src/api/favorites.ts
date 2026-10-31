import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import { useAuthStore } from '../store/authStore';

export const useFavoriteIds = () => {
  const isAuthenticated = useAuthStore(state => state.isAuthenticated);
  return useQuery<number[]>({
    queryKey: ['favorites'],
    queryFn: () => apiClient.get('/api/favorites').then(res => res.data),
    enabled: isAuthenticated,
    staleTime: 60 * 1000,
  });
};

export const useToggleFavorite = () => {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, { dishId: number; isFavorite: boolean }, { previous?: number[] }>({
    mutationFn: ({ dishId, isFavorite }) =>
      isFavorite
        ? apiClient.post(`/api/favorites/${dishId}`).then(res => res.data)
        : apiClient.delete(`/api/favorites/${dishId}`).then(res => res.data),
    onMutate: async ({ dishId, isFavorite }) => {
      await queryClient.cancelQueries({ queryKey: ['favorites'] });
      const previous = queryClient.getQueryData<number[]>(['favorites']);
      const current = previous ?? [];
      queryClient.setQueryData<number[]>(
        ['favorites'],
        isFavorite ? [dishId, ...current.filter(id => id !== dishId)] : current.filter(id => id !== dishId)
      );
      return { previous };
    },
    onError: (_err, _vars, context) => {
      if (context?.previous) queryClient.setQueryData(['favorites'], context.previous);
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['favorites'] });
    },
  });
};
