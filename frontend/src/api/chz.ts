import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';

export type ChzStatus = 'draft' | 'sent' | 'ok' | 'error';

export interface ChzState {
  enabled: boolean;
  sandbox: boolean;
  inn: string;
  productGroups: string[];
  signedIn: boolean;
  expiresAt: string | null;
  actions: { key: string; title: string }[];
  groups: { key: string; title: string }[];
}

export interface ChzDocumentRow {
  id: number;
  productGroup: string;
  action: string;
  actionDate: string;
  documentNumber: string;
  codesCount: number;
  status: ChzStatus;
  chzDocumentId: string | null;
  result: string | null;
  comment: string | null;
  createdAt: string;
  sentAt: string | null;
  checkedAt: string | null;
}

export interface ChzDocumentDetail extends ChzDocumentRow {
  codes: string[];
  productDocument: string | null;
}

export const CHZ_STATUS: Record<ChzStatus, { label: string; color: string }> = {
  draft: { label: 'Не отправлен', color: '#7B776F' },
  sent: { label: 'Проверяется', color: '#2563EB' },
  ok: { label: 'Выведено', color: '#2E9E5B' },
  error: { label: 'Ошибка', color: '#E2462C' },
};

export const useChzState = () =>
  useQuery<ChzState>({
    queryKey: ['chz', 'status'],
    queryFn: () => apiClient.get('/api/chz/status').then(r => r.data),
  });

export const useChzDocuments = () =>
  useQuery<ChzDocumentRow[]>({
    queryKey: ['chz', 'documents'],
    queryFn: () => apiClient.get('/api/chz/documents').then(r => r.data),
  });

export const fetchChzDocument = (id: number) => apiClient.get<ChzDocumentDetail>(`/api/chz/documents/${id}`).then(r => r.data);

export const useSaveChzSettings = () => {
  const qc = useQueryClient();
  return useMutation<ChzState, Error, { enabled: boolean; sandbox: boolean; inn: string; productGroups: string[] }>({
    mutationFn: (data) => apiClient.put('/api/chz/settings', data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['chz'] }),
  });
};

export const chzAuthStart = () => apiClient.post<{ uuid: string; data: string }>('/api/chz/auth/start').then(r => r.data);
export const chzAuthComplete = (uuid: string, signature: string) =>
  apiClient.post<{ signedIn: boolean; expiresAt: string }>('/api/chz/auth/complete', { uuid, signature }).then(r => r.data);
export const chzLogout = () => apiClient.post('/api/chz/auth/logout');

export const chzPrepare = (data: { productGroup: string; action: string; date: string; codes: string; comment?: string }) =>
  apiClient.post<ChzDocumentDetail>('/api/chz/withdrawals', data).then(r => r.data);
export const chzSubmit = (id: number, signature: string) =>
  apiClient.post<ChzDocumentDetail>(`/api/chz/withdrawals/${id}/submit`, { signature }).then(r => r.data);
export const chzRefresh = (id: number) =>
  apiClient.post<ChzDocumentDetail>(`/api/chz/withdrawals/${id}/refresh`).then(r => r.data);
export const chzDelete = (id: number) => apiClient.delete(`/api/chz/documents/${id}`);
