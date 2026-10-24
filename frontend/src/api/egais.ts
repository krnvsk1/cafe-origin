import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { StockUnit } from './inventory';

export type EgaisStatus = 'awaiting_regid' | 'ready' | 'act_sent' | 'accepted' | 'rejected' | 'differences' | 'error';

export interface EgaisState {
  enabled: boolean;
  configured: boolean;
  utmUrl: string;
  fsrarId: string | null;
  reachable: boolean;
  autoSyncMinutes: number;
  sendActs: boolean;
  deleteProcessed: boolean;
  lastSyncAt: string | null;
  lastSyncError: string | null;
  toProcess: number;
  awaitingRegId: number;
  awaitingTicket: number;
}

export interface EgaisDocumentRow {
  id: number;
  number: string;
  date: string | null;
  shipperName: string | null;
  shipperInn: string | null;
  wbRegId: string | null;
  status: EgaisStatus;
  actDecision: string | null;
  actSentAt: string | null;
  resultComment: string | null;
  stockDocumentId: number | null;
  createdAt: string;
  linesCount: number;
  unmapped: number;
  total: number;
}

export interface EgaisLine {
  id: number;
  identity: string;
  productName: string;
  alcCode: string | null;
  capacity: number | null;
  alcVolume: number | null;
  unitType: string | null;
  quantity: number;
  price: number;
  informF2RegId: string | null;
  realQuantity: number | null;
  stockItemId: number | null;
  stockItemName: string | null;
  stockUnit: StockUnit | null;
  stockQuantity: number | null;
  conversionError: string | null;
}

export interface EgaisDocumentDetail {
  id: number;
  number: string;
  date: string | null;
  shipperName: string | null;
  shipperInn: string | null;
  shipperFsrarId: string | null;
  wbRegId: string | null;
  status: EgaisStatus;
  actNumber: string | null;
  actDecision: string | null;
  actNote: string | null;
  actSentAt: string | null;
  resultComment: string | null;
  resultAt: string | null;
  stockDocumentId: number | null;
  lines: EgaisLine[];
}

export const EGAIS_STATUS: Record<EgaisStatus, { label: string; color: string }> = {
  awaiting_regid: { label: 'Ждём регистрацию', color: '#7B776F' },
  ready: { label: 'К приёмке', color: '#C77700' },
  act_sent: { label: 'Акт отправлен', color: '#2563EB' },
  accepted: { label: 'Принята', color: '#2E9E5B' },
  differences: { label: 'С расхождениями', color: '#7A4DFF' },
  rejected: { label: 'Отказ', color: '#E2462C' },
  error: { label: 'Ошибка ЕГАИС', color: '#E2462C' },
};

export const useEgaisState = () =>
  useQuery<EgaisState>({
    queryKey: ['egais', 'status'],
    queryFn: () => apiClient.get('/api/egais/status').then(r => r.data),
    refetchInterval: 60000,
  });

export const useEgaisDocuments = (status: string) =>
  useQuery<EgaisDocumentRow[]>({
    queryKey: ['egais', 'documents', status],
    queryFn: () => apiClient.get('/api/egais/documents', { params: { status: status || undefined } }).then(r => r.data),
  });

export const useEgaisDocument = (id: number | null) =>
  useQuery<EgaisDocumentDetail>({
    queryKey: ['egais', 'document', id],
    queryFn: () => apiClient.get(`/api/egais/documents/${id}`).then(r => r.data),
    enabled: !!id,
  });

export const useEgaisSync = () => {
  const qc = useQueryClient();
  return useMutation<{ wayBills: number; regInfos: number; tickets: number; skipped: number; errors: string[]; resendRequested: number }, Error, void>({
    mutationFn: () => apiClient.post('/api/egais/sync').then(r => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: ['egais'] }),
  });
};

export const useEgaisMapping = () => {
  const qc = useQueryClient();
  return useMutation<EgaisDocumentDetail, Error, { id: number; lines: { lineId: number; stockItemId: number | null }[] }>({
    mutationFn: ({ id, lines }) => apiClient.put(`/api/egais/documents/${id}/mapping`, { lines }).then(r => r.data),
    onSuccess: (data) => {
      qc.setQueryData(['egais', 'document', data.id], data);
      qc.invalidateQueries({ queryKey: ['egais', 'documents'] });
      qc.invalidateQueries({ queryKey: ['inventory', 'items'] });
    },
  });
};

export const useEgaisAct = () => {
  const qc = useQueryClient();
  return useMutation<EgaisDocumentDetail, Error, { id: number; decision: 'accept' | 'reject' | 'differences'; note?: string; lines: { lineId: number; realQuantity: number | null }[] }>({
    mutationFn: ({ id, ...body }) => apiClient.post(`/api/egais/documents/${id}/act`, body).then(r => r.data),
    onSettled: () => {
      qc.invalidateQueries({ queryKey: ['egais'] });
      qc.invalidateQueries({ queryKey: ['inventory'] });
    },
  });
};

// ================= Настройки =================

export interface EgaisSettings {
  enabled: boolean;
  utmUrl: string;
  fsrarId: string;
  autoSyncMinutes: number;
  deleteProcessed: boolean;
  sendActs: boolean;
  savedInAdmin?: boolean;
  updatedAt?: string | null;
}

export const useEgaisSettings = (enabled = true) =>
  useQuery<EgaisSettings>({
    queryKey: ['egais', 'settings'],
    queryFn: () => apiClient.get('/api/egais/settings').then(r => r.data),
    enabled,
  });

export const useSaveEgaisSettings = () => {
  const qc = useQueryClient();
  return useMutation<EgaisSettings, Error, Omit<EgaisSettings, 'savedInAdmin' | 'updatedAt'>>({
    mutationFn: (data) => apiClient.put('/api/egais/settings', data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['egais'] }),
  });
};

export const testUtmConnection = (utmUrl: string) =>
  apiClient.post<{ ok: boolean; message: string }>('/api/egais/settings/test', { utmUrl }).then(r => r.data);

export const requestUnaccepted = () =>
  apiClient.post<{ message: string }>('/api/egais/request-unaccepted').then(r => r.data);

export interface EgaisDiagnostics {
  lastNoAnswerAt: string | null;
  noAnswer: { wbRegId: string; number: string | null; date: string | null; shipperFsrarId: string | null; action: string; inService: boolean }[];
  inbox: { type: string; url: string; seen: boolean }[];
  inboxError: string | null;
  lastSyncError: string | null;
}

export const fetchEgaisDiagnostics = () =>
  apiClient.get<EgaisDiagnostics>('/api/egais/diagnostics').then(r => r.data);
