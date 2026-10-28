import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';

export type MercuryStatus = 'new' | 'processing' | 'accepted' | 'error' | 'ignored' | 'external';

export interface MercuryState {
  enabled: boolean;
  sandbox: boolean;
  hasApiKey: boolean;
  login: string;
  hasPassword: boolean;
  issuerId: string;
  initiatorLogin: string;
  enterpriseGuid: string;
  autoSyncMinutes: number;
  initialDays: number;
  lastSyncAt: string | null;
  lastError: string | null;
  newCount: number;
}

export interface MercuryDocument {
  id: number;
  uuid: string;
  issueDate: string | null;
  vetStatus: string | null;
  productName: string | null;
  volume: number;
  unitName: string | null;
  productionDate: string | null;
  expiryDate: string | null;
  consignorName: string | null;
  waybill: string | null;
  status: MercuryStatus;
  error: string | null;
  stockEntryGuid: string | null;
  processedAt: string | null;
}

export interface MercurySettingsInput {
  enabled: boolean;
  sandbox: boolean;
  apiKey: string | null;
  login: string;
  password: string | null;
  issuerId: string;
  initiatorLogin: string;
  enterpriseGuid: string;
  autoSyncMinutes: number;
  initialDays: number;
}

export const MERCURY_STATUS: Record<MercuryStatus, { label: string; color: string }> = {
  new: { label: 'Не погашен', color: '#2563EB' },
  processing: { label: 'Гасится…', color: '#C77700' },
  accepted: { label: 'Погашен', color: '#2E9E5B' },
  error: { label: 'Ошибка', color: '#E2462C' },
  ignored: { label: 'Скрыт', color: '#7B776F' },
  external: { label: 'Погашен вне сервиса', color: '#2E9E5B' },
};

export const useMercuryState = () =>
  useQuery<MercuryState>({
    queryKey: ['mercury', 'status'],
    queryFn: () => apiClient.get('/api/mercury/status').then(r => r.data),
    refetchInterval: 60_000,
  });

export const useMercuryDocuments = (filter: 'new' | 'done' | 'ignored' | 'all') =>
  useQuery<MercuryDocument[]>({
    queryKey: ['mercury', 'documents', filter],
    queryFn: () => apiClient.get('/api/mercury/documents', { params: { status: filter } }).then(r => r.data),
  });

export const saveMercurySettings = (data: MercurySettingsInput) => apiClient.put<MercuryState>('/api/mercury/settings', data).then(r => r.data);
export const testMercurySettings = (data: MercurySettingsInput) =>
  apiClient.post<{ ok: boolean; message: string }>('/api/mercury/settings/test', data, { timeout: 120000 }).then(r => r.data);
export const mercurySync = () =>
  apiClient.post<{ received: number; total: number; external: number }>('/api/mercury/sync', null, { timeout: 300000 }).then(r => r.data);
export const mercuryAccept = (ids: number[]) =>
  apiClient.post<{ accepted: number; errors: string[] }>('/api/mercury/accept', { ids }, { timeout: 600000 }).then(r => r.data);
export const mercuryIgnore = (id: number) => apiClient.post(`/api/mercury/documents/${id}/ignore`);
export const mercuryRestore = (id: number) => apiClient.post(`/api/mercury/documents/${id}/restore`);
