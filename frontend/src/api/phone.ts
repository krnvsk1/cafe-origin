import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';
import type { User } from '../types';

/** Нужно ли подтверждать телефон по SMS перед доставкой (SMS-сервис подключён) */
export const usePhoneRequired = () =>
  useQuery<{ required: boolean }>({
    queryKey: ['auth-phone'],
    queryFn: () => apiClient.get('/api/auth/phone').then(r => r.data),
    staleTime: 10 * 60 * 1000,
  });

export const sendPhoneCode = (phone: string) =>
  apiClient.post<{ message: string; phone: string }>('/api/auth/phone/send-code', { phone }).then(r => r.data);

export const verifyPhoneCode = (phone: string, code: string) =>
  apiClient.post<{ user: User }>('/api/auth/phone/verify', { phone, code }).then(r => r.data.user);
