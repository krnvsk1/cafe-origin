import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import { useAuthStore } from '../store/authStore';

export interface Review {
  id: number;
  orderId: number;
  rating: number;
  comment: string | null;
  adminReply: string | null;
  repliedAt: string | null;
  createdAt: string;
  customerName?: string | null;
}

export interface ReviewsAdminResponse {
  total: number;
  page: number;
  pageSize: number;
  summary: { count: number; average: number; distribution: { rating: number; count: number }[] };
  items: { review: Review; orderTotal: number; items: string[] }[];
}

export interface ReviewsFilter {
  rating?: number;
  withComment?: boolean;
  unanswered?: boolean;
  page?: number;
}

export const useMyReviews = () => {
  const isAuthenticated = useAuthStore(state => state.isAuthenticated);
  return useQuery<Review[]>({
    queryKey: ['reviews', 'my'],
    queryFn: () => apiClient.get('/api/reviews/my').then(res => res.data),
    enabled: isAuthenticated,
    staleTime: 60 * 1000,
  });
};

export const useCreateReview = () => {
  const queryClient = useQueryClient();
  return useMutation<Review, Error, { orderId: number; rating: number; comment?: string }>({
    mutationFn: (data) => apiClient.post('/api/reviews', data).then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['reviews'] });
    },
  });
};

export const useAdminReviews = (filter: ReviewsFilter) =>
  useQuery<ReviewsAdminResponse>({
    queryKey: ['reviews', 'admin', filter],
    queryFn: () => apiClient.get('/api/reviews', { params: { ...filter, pageSize: 20 } }).then(res => res.data),
  });

export const useReplyReview = () => {
  const queryClient = useQueryClient();
  return useMutation<Review, Error, { id: number; reply: string }>({
    mutationFn: ({ id, reply }) => apiClient.post(`/api/reviews/${id}/reply`, { reply }).then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['reviews'] });
    },
  });
};
